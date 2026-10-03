using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarThunder;

/// <summary>
/// Talks to the Saitek Multi Panel device plugin's local API: writes values to the LCD
/// and listens (push, no polling) for selector knob changes.
/// </summary>
public sealed class PanelLink : IDisposable
{
    public const string DeviceNamespace = "pz";
    private static readonly string[] Modes = ["alt", "vs", "ias", "hdg", "crs"];

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _modes = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _watch;

    public PanelLink(int port)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Raised when a panel's selector moves.</summary>
    public event Action? ModeChanged;

    /// <summary>True for the Multi Panel's 5 knob dials (ALT, VS, IAS, HDG, CRS).</summary>
    public static bool IsPanelDial(string device, string controller, int position) =>
        controller == "Encoder" && device.StartsWith(DeviceNamespace + "-") && position is >= 0 and <= 4;

    /// <summary>Whether the panel currently shows this dial's value (ALT and VS share the display).</summary>
    public bool IsShown(string device, int dial)
    {
        EnsureWatching();
        if (!_modes.TryGetValue(device, out var mode))
            return false;
        var selector = Array.IndexOf(Modes, mode);
        if (selector < 0)
            return false;
        return selector == dial || (selector <= 1 && dial <= 1);
    }

    /// <summary>Show text on the LCD for a knob dial (null clears). Row null = the dial's default row.</summary>
    public async Task ShowAsync(int dial, string? text, string? row)
    {
        var body = new JsonObject { ["dial"] = dial, ["text"] = text };
        if (row is not null)
            body["row"] = row;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var _ = await _http.PostAsync("dial", content, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Device plugin not running; nothing to show on.
        }
    }

    private void EnsureWatching() => _watch ??= Task.Run(() => WatchAsync(_cts.Token));

    /// <summary>Holds a /watch stream open; reconnects every few seconds if the device plugin isn't up.</summary>
    private async Task WatchAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await _http.GetAsync("watch", HttpCompletionOption.ResponseHeadersRead, ct);
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    using var doc = JsonDocument.Parse(line);
                    var device = doc.RootElement.GetProperty("device").GetString()!;
                    var mode = doc.RootElement.GetProperty("mode").GetString()!;
                    _modes[device] = mode;
                    ModeChanged?.Invoke();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException && !ct.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_modes.IsEmpty)
            {
                _modes.Clear();
                ModeChanged?.Invoke();
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ContinueWith(_ => { });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
