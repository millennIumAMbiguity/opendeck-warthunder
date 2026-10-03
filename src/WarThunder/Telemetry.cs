using System.Text.Json;

namespace WarThunder;

/// <summary>One snapshot of War Thunder's /state and /indicators (only the ones that were requested).</summary>
public sealed class Telemetry
{
    private readonly Dictionary<string, double> _state;
    private readonly Dictionary<string, double> _indicators;
    private readonly Dictionary<string, string> _text;

    public Telemetry(Dictionary<string, double> state, Dictionary<string, double> indicators, Dictionary<string, string> text, bool valid)
    {
        _state = state;
        _indicators = indicators;
        _text = text;
        Valid = valid;
    }

    /// <summary>String values from either endpoint, e.g. "type" (aircraft id) from /indicators.</summary>
    public string? Text(string key) => _text.TryGetValue(key, out var v) ? v : null;

    /// <summary>False when the game runs but you're not flying (hangar, tank, spectating).</summary>
    public bool Valid { get; }

    public double? State(string key) => _state.TryGetValue(key, out var v) ? v : null;

    public double? Indicator(string key) => _indicators.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Reads the game's local telemetry server (http://127.0.0.1:8111).</summary>
public sealed class TelemetryClient : IDisposable
{
    private readonly HttpClient _http;

    public TelemetryClient(int port = 8111)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromMilliseconds(800),
        };
    }

    /// <summary>Fetches only the endpoints that shown actions need. Null when the game isn't running.</summary>
    public async Task<Telemetry?> ReadAsync(bool state, bool indicators, CancellationToken ct)
    {
        try
        {
            var text = new Dictionary<string, string>();
            var stateTask = state ? FetchAsync("state", text, ct) : Task.FromResult<(Dictionary<string, double>, bool)?>(null);
            var indicatorsTask = indicators ? FetchAsync("indicators", text, ct) : Task.FromResult<(Dictionary<string, double>, bool)?>(null);
            var s = await stateTask;
            var i = await indicatorsTask;
            if ((state && s is null) || (indicators && i is null))
                return null;
            var valid = (s?.Item2 ?? true) && (i?.Item2 ?? true);
            return new Telemetry(s?.Item1 ?? [], i?.Item1 ?? [], text, valid);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<(Dictionary<string, double>, bool)?> FetchAsync(string path, Dictionary<string, string> text, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var values = new Dictionary<string, double>();
        var valid = false;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number)
                values[prop.Name] = prop.Value.GetDouble();
            else if (prop.Name == "valid")
                valid = prop.Value.ValueKind == JsonValueKind.True;
            else if (prop.Value.ValueKind == JsonValueKind.String)
                lock (text)
                    text[prop.Name] = prop.Value.GetString()!;
        }
        return (values, valid);
    }

    public void Dispose() => _http.Dispose();
}
