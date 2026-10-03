using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenDeck.Sdk;

namespace WarThunder;

/// <summary>
/// Tracks placed War Thunder actions and polls the game only while at least one of them is on screen.
/// Stream Deck keys/dials count as shown while placed on the current page; Multi Panel knob dials
/// only while the selector is on their position.
/// </summary>
public sealed class ActionHub : IDisposable
{
    public const string FlightDataUuid = "io.github.millenniumambiguity.warthunder.flightdata";
    public const string IndicatorUuid = "io.github.millenniumambiguity.warthunder.indicator";
    public const string SendKeyUuid = "io.github.millenniumambiguity.warthunder.sendkey";
    public const string TextUuid = "io.github.millenniumambiguity.warthunder.text";

    private static readonly TimeSpan Interval =
        TimeSpan.FromMilliseconds(int.TryParse(Environment.GetEnvironmentVariable("WT_INTERVAL_MS"), out var ms) ? Math.Max(50, ms) : 250);
    private static readonly TimeSpan NotFlyingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GameClosedInterval = TimeSpan.FromSeconds(3);

    private readonly OpenDeckConnection _deck;
    private readonly TelemetryClient _telemetry;
    private readonly PanelLink _panel;
    private readonly ConcurrentDictionary<string, Instance> _instances = new();
    private readonly object _loopLock = new();
    private CancellationTokenSource? _loop;
    private int _resyncScheduled;

    public ActionHub(OpenDeckConnection deck, TelemetryClient telemetry, PanelLink panel)
    {
        _deck = deck;
        _telemetry = telemetry;
        _panel = panel;
        _panel.ModeChanged += Reevaluate;
    }

    private sealed class Instance
    {
        public required string Context;
        public required string Action;
        public required string Device;
        public required string Controller;
        public required int Position;

        public Field Field = Field.All[0];
        public string Unit = "";
        public string Label = "";
        public bool ShowLabel = true;
        public bool ShowUnit = true;
        public string? Row;
        public bool Above = true;
        public double Threshold;
        public string Color = "#ffb000";

        /// <summary>Held while the key/dial is pressed, or tapped once on press when <see cref="Tap"/>.</summary>
        public KeyCombo? PressKey;
        /// <summary>Tap once on press instead of holding; for latching switches (A/T, flaps lever).</summary>
        public bool Tap;
        /// <summary>Clicked once per knob step.</summary>
        public KeyCombo? ClockwiseKey;
        public KeyCombo? AnticlockwiseKey;
        public TextTemplate? Template;

        public string? LastTitle;
        public string? LastPanelText;
        public bool? LastLit;

        public bool IsIndicator => Action == IndicatorUuid;
        public bool UsesTelemetry => Action != SendKeyUuid;
        public bool NeedsState => Template?.NeedsState ?? Field.Source == Source.State;
        public bool NeedsIndicators => Template?.NeedsIndicators ?? Field.Source == Source.Indicators;
        public bool OnPanelDial => PanelLink.IsPanelDial(Device, Controller, Position);
    }

    public async Task OnEventAsync(string name, JsonElement message)
    {
        if (!message.TryGetProperty("action", out var a) || a.GetString() is not (FlightDataUuid or IndicatorUuid or SendKeyUuid or TextUuid) || a.GetString() is not { } action)
            return;

        var context = message.GetProperty("context").GetString()!;
        var payload = message.GetProperty("payload");

        switch (name)
        {
            case "willAppear":
            {
                var instance = new Instance
                {
                    Context = context,
                    Action = action,
                    Device = message.GetProperty("device").GetString()!,
                    Controller = payload.TryGetProperty("controller", out var c) ? c.GetString() ?? "Keypad" : "Keypad",
                    Position = Position(payload),
                };
                ApplySettings(instance, payload.GetProperty("settings"));
                _instances[context] = instance;
                await RenderAsync(instance, null);
                ScheduleResync();
                break;
            }
            case "willDisappear":
                if (_instances.TryRemove(context, out var gone) && gone.OnPanelDial)
                    await _panel.ShowAsync(gone.Position, null, gone.Row);
                break;
            case "didReceiveSettings":
                if (_instances.TryGetValue(context, out var changed))
                {
                    if (changed.OnPanelDial)
                        await _panel.ShowAsync(changed.Position, null, changed.Row);
                    ApplySettings(changed, payload.GetProperty("settings"));
                    await RenderAsync(changed, null);
                }
                break;
            case "keyDown" or "dialDown":
                if (_instances.TryGetValue(context, out var pressed) && pressed.PressKey is { } down)
                {
                    if (pressed.Tap)
                        await KeySender.ClickAsync(down);
                    else
                        KeySender.Down(down);
                }
                return;
            case "keyUp" or "dialUp":
                if (_instances.TryGetValue(context, out var released) && released.PressKey is { } up && !released.Tap)
                    KeySender.Up(up);
                return;
            case "dialRotate":
                if (_instances.TryGetValue(context, out var turned))
                {
                    var ticks = payload.GetProperty("ticks").GetInt32();
                    if ((ticks > 0 ? turned.ClockwiseKey : turned.AnticlockwiseKey) is { } key)
                        await KeySender.ClickAsync(key, Math.Abs(ticks));
                }
                return;
            default:
                return;
        }

        Reevaluate();
    }

    /// <summary>
    /// OpenDeck can drop title/image updates that arrive while it is still drawing a freshly loaded
    /// profile. Once things have settled, forget what was sent so the next poll sends everything again.
    /// </summary>
    private void ScheduleResync()
    {
        if (Interlocked.Exchange(ref _resyncScheduled, 1) == 1)
            return;
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _resyncScheduled, 0);
            foreach (var i in _instances.Values)
            {
                i.LastTitle = null;
                i.LastPanelText = null;
                i.LastLit = null;
            }
        });
    }

    /// <summary>Encoders report their index as the column; keys as row/column on the grid.</summary>
    private static int Position(JsonElement payload)
    {
        var coords = payload.GetProperty("coordinates");
        return coords.GetProperty("column").GetInt32();
    }

    private static void ApplySettings(Instance i, JsonElement settings)
    {
        var s = settings.ValueKind == JsonValueKind.Object ? JsonNode.Parse(settings.GetRawText())!.AsObject() : [];
        i.Field = Field.Get(Str(s, "field") ?? (i.IsIndicator ? "gear" : "ias"));
        var units = Units.For(i.Field.Kind);
        i.Unit = Str(s, "unit") is { } u && units.Contains(u) ? u : units[0];
        i.Label = Str(s, "label") ?? i.Field.Label;
        i.ShowLabel = Bool(s, "showLabel") ?? true;
        i.ShowUnit = Bool(s, "showUnit") ?? true;
        i.Row = Str(s, "row");
        i.Above = Str(s, "op") != "below";
        i.Threshold = s["threshold"] is JsonValue t && (t.TryGetValue<double>(out var d) ||
            (t.TryGetValue<string>(out var str) && double.TryParse(str, CultureInfo.InvariantCulture, out d))) ? d : 50;
        i.Color = Str(s, "color") ?? "#ffb000";
        i.PressKey = KeyCombo.TryParse(Str(s, "key"));
        i.Tap = Bool(s, "tap") ?? false;
        i.ClockwiseKey = KeyCombo.TryParse(Str(s, "keyCw"));
        i.AnticlockwiseKey = KeyCombo.TryParse(Str(s, "keyCcw"));
        i.Template = i.Action == TextUuid ? new TextTemplate(Str(s, "template") ?? DefaultTemplate) : null;
        i.LastTitle = null;
        i.LastPanelText = null;
        i.LastLit = null;
    }

    private const string DefaultTemplate = "{aircraft}\\nIAS {ias}\\nALT {alt}";

    private static string? Str(JsonObject s, string key) =>
        (s[key] as JsonValue)?.TryGetValue<string>(out var v) == true && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static bool? Bool(JsonObject s, string key) => s[key]?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private bool IsShown(Instance i) => i.UsesTelemetry && (!i.OnPanelDial || _panel.IsShown(i.Device, i.Position));

    private void Reevaluate()
    {
        var anyShown = _instances.Values.Any(IsShown);
        lock (_loopLock)
        {
            if (anyShown && _loop is null)
            {
                _loop = new CancellationTokenSource();
                _ = RunLoopAsync(_loop.Token);
                Console.WriteLine("Telemetry polling started");
            }
            else if (!anyShown && _loop is not null)
            {
                _loop.Cancel();
                _loop = null;
                Console.WriteLine("Telemetry polling stopped");
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var shown = _instances.Values.Where(IsShown).ToList();
                if (shown.Count == 0)
                    break;

                var t = await _telemetry.ReadAsync(
                    shown.Any(i => i.NeedsState),
                    shown.Any(i => i.NeedsIndicators),
                    ct);

                foreach (var instance in shown)
                    await RenderAsync(instance, t);

                await Task.Delay(t is null ? GameClosedInterval : t.Valid ? Interval : NotFlyingInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Telemetry loop failed: {ex}");
        }
        finally
        {
            lock (_loopLock)
            {
                if (_loop?.Token == ct)
                    _loop = null;
            }
        }
    }

    /// <summary>Updates title/image/LCD, sending only what changed. Null or invalid telemetry = no data.</summary>
    private async Task RenderAsync(Instance i, Telemetry? t)
    {
        if (!i.UsesTelemetry)
            return;

        if (i.Template is { } template)
        {
            var text = template.Render(t);
            if (text != i.LastTitle)
            {
                i.LastTitle = text;
                await _deck.SetTitleAsync(i.Context, text);
            }
            return;
        }

        var raw = t is { Valid: true } ? i.Field.Read(t) : null;
        double? value = raw is { } r ? Units.Convert(r, i.Unit) : null;

        if (i.IsIndicator)
        {
            var lit = value is { } v && (i.Above ? v > i.Threshold : v < i.Threshold);
            if (i.LastTitle != i.Label)
            {
                i.LastTitle = i.Label;
                await _deck.SetTitleAsync(i.Context, i.Label);
            }
            if (lit != i.LastLit)
            {
                i.LastLit = lit;
                Console.WriteLine($"{i.Label} ({i.Field.Id} {(i.Above ? ">" : "<")} {i.Threshold}): {(lit ? "on" : "off")} [value {value?.ToString() ?? "none"}]");
                await _deck.SetImageAsync(i.Context, lit ? LitImage(i.Color) : DarkImage(i.Color));
            }
            return;
        }

        var sb = new StringBuilder();
        if (i.ShowLabel)
            sb.Append(i.Label).Append('\n');
        sb.Append(value is { } val ? Units.ForTitle(val, i.Field.Kind) : "--");
        var unitLabel = Units.Label(i.Unit, i.Field.Kind);
        if (i.ShowUnit && value is not null && unitLabel != "")
            sb.Append('\n').Append(unitLabel);
        var title = sb.ToString();
        if (title != i.LastTitle)
        {
            i.LastTitle = title;
            await _deck.SetTitleAsync(i.Context, title);
        }

        if (i.OnPanelDial)
        {
            var panelText = value is { } pv ? Units.ForPanel(pv, i.Field.Kind) : null;
            if (panelText != i.LastPanelText)
            {
                i.LastPanelText = panelText;
                await _panel.ShowAsync(i.Position, panelText, i.Row);
            }
        }
    }

    /// <summary>Bright fill: reads as "on" for the Multi Panel LED (it lights on bright key images).</summary>
    private static string LitImage(string color) => Svg(
        $"<rect width='144' height='144' rx='16' fill='{Escape(color)}'/>");

    /// <summary>Near-black with a faint outline, so the key stays visible but the LED reads as off.</summary>
    private static string DarkImage(string color) => Svg(
        $"<rect width='144' height='144' fill='#000'/><rect x='6' y='6' width='132' height='132' rx='12' fill='none' stroke='{Escape(color)}' stroke-opacity='0.35' stroke-width='4'/>");

    private static string Svg(string body) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='144' height='144' viewBox='0 0 144 144'>{body}</svg>"));

    /// <summary>Colors come from settings; only allow #rgb/#rrggbb so nothing else ends up in the SVG.</summary>
    private static string Escape(string color) =>
        System.Text.RegularExpressions.Regex.IsMatch(color, "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? color : "#ffb000";

    public void Dispose()
    {
        lock (_loopLock)
        {
            _loop?.Cancel();
            _loop = null;
        }
    }
}
