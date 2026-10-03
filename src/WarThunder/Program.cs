using OpenDeck.Sdk;
using WarThunder;

if (args.Contains("--dump"))
{
    // Prints what the game currently reports, to check field names against Fields.cs.
    using var client = new TelemetryClient();
    var t = await client.ReadAsync(true, true, CancellationToken.None);
    if (t is null)
    {
        Console.WriteLine("War Thunder telemetry not reachable on 127.0.0.1:8111 (game not running?)");
        return 1;
    }
    Console.WriteLine($"valid={t.Valid}");
    foreach (var f in Field.All)
        Console.WriteLine($"{f.Id,-9} {f.Key,-16} {f.Read(t)?.ToString("0.##") ?? "(missing)"}");
    return 0;
}

PluginArgs pluginArgs;
try
{
    pluginArgs = PluginArgs.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run with --dump to print current War Thunder telemetry.");
    return 1;
}

Console.WriteLine($"Starting {pluginArgs.PluginUuid}, OpenDeck port {pluginArgs.Port}");

var panelPort = int.TryParse(Environment.GetEnvironmentVariable("MULTIPANEL_API_PORT"), out var p) ? p : 47070;
using var cts = new CancellationTokenSource();
await using var deck = new OpenDeckConnection(pluginArgs);
using var telemetry = new TelemetryClient();
using var panel = new PanelLink(panelPort);
using var hub = new ActionHub(deck, telemetry, panel);
deck.EventHandler = hub.OnEventAsync;

await deck.ConnectAsync(cts.Token);
try
{
    await deck.RunAsync(cts.Token);
}
catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException)
{
    Console.WriteLine($"Connection ended: {ex.Message}");
}

Console.WriteLine("Shutting down");
return 0;
