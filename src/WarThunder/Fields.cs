using System.Globalization;

namespace WarThunder;

/// <summary>Which War Thunder endpoint a value comes from.</summary>
public enum Source { State, Indicators }

/// <summary>Physical quantity, decides the available units and formatting.</summary>
public enum Kind { Speed, Altitude, VerticalSpeed, Heading, Mach, Angle, GForce, Percent, Rpm, Mass, Temperature }

/// <summary>
/// One telemetry value. <paramref name="Key"/> is the exact JSON key from
/// http://127.0.0.1:8111/state or /indicators. Fuel % is computed from two keys.
/// </summary>
public sealed record Field(string Id, string Label, Source Source, string Key, Kind Kind)
{
    public static readonly Field[] All =
    [
        new("ias", "IAS", Source.State, "IAS, km/h", Kind.Speed),
        new("tas", "TAS", Source.State, "TAS, km/h", Kind.Speed),
        new("mach", "MACH", Source.State, "M", Kind.Mach),
        new("alt", "ALT", Source.State, "H, m", Kind.Altitude),
        new("vs", "V/S", Source.State, "Vy, m/s", Kind.VerticalSpeed),
        new("hdg", "HDG", Source.Indicators, "compass", Kind.Heading),
        new("aoa", "AoA", Source.State, "AoA, deg", Kind.Angle),
        new("g", "G", Source.State, "Ny", Kind.GForce),
        new("throttle", "THR", Source.State, "throttle 1, %", Kind.Percent),
        new("rpm", "RPM", Source.State, "RPM 1", Kind.Rpm),
        new("flaps", "FLAPS", Source.State, "flaps, %", Kind.Percent),
        new("gear", "GEAR", Source.State, "gear, %", Kind.Percent),
        new("airbrake", "BRAKE", Source.State, "airbrake, %", Kind.Percent),
        new("fuel", "FUEL", Source.State, "Mfuel, kg", Kind.Percent), // Mfuel / Mfuel0
        new("fuelmass", "FUEL", Source.State, "Mfuel, kg", Kind.Mass),
        new("oil", "OIL", Source.State, "oil temp 1, C", Kind.Temperature),
    ];

    public static Field Get(string? id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];

    /// <summary>Reads the raw value (in War Thunder's metric units) from a telemetry snapshot.</summary>
    public double? Read(Telemetry t)
    {
        if (Id == "fuel")
            return t.State("Mfuel, kg") is { } fuel && t.State("Mfuel0, kg") is > 0 and var full ? 100 * fuel / full : null;
        return Source == Source.State ? t.State(Key) : t.Indicator(Key);
    }
}

/// <summary>Unit conversion and text formatting for a field.</summary>
public static class Units
{
    /// <summary>Unit ids per kind; first one is War Thunder's native unit and the default.</summary>
    public static string[] For(Kind kind) => kind switch
    {
        Kind.Speed => ["kmh", "kt", "mph"],
        Kind.Altitude => ["m", "ft"],
        Kind.VerticalSpeed => ["ms", "fpm"],
        Kind.Mass => ["kg", "lb"],
        _ => [""],
    };

    public static string Label(string unit, Kind kind) => unit switch
    {
        "kmh" => "km/h",
        "kt" => "kt",
        "mph" => "mph",
        "m" => "m",
        "ft" => "ft",
        "ms" => "m/s",
        "fpm" => "ft/min",
        "kg" => "kg",
        "lb" => "lb",
        _ => kind switch
        {
            Kind.Percent => "%",
            Kind.Angle => "°",
            Kind.Temperature => "°C",
            _ => "",
        },
    };

    public static double Convert(double value, string unit) => unit switch
    {
        "kt" => value * 0.539957,
        "mph" => value * 0.621371,
        "ft" => value * 3.28084,
        "fpm" => value * 196.850,
        "lb" => value * 2.20462,
        _ => value,
    };

    /// <summary>Text for a Stream Deck title: a decimal where it matters (Mach, G, AoA).</summary>
    public static string ForTitle(double value, Kind kind) => kind switch
    {
        Kind.Mach => value.ToString("0.00", CultureInfo.InvariantCulture),
        Kind.GForce or Kind.Angle => value.ToString("0.0", CultureInfo.InvariantCulture),
        Kind.Heading => Heading(value),
        _ => Math.Round(value).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Text for the Multi Panel LCD: digits only, no decimal point. Mach is shown x100
    /// (0.85 -> 85), G and AoA x10 (6.5 -> 65), heading as 3 digits.
    /// </summary>
    public static string ForPanel(double value, Kind kind) => kind switch
    {
        Kind.Mach => Math.Round(value * 100).ToString(CultureInfo.InvariantCulture),
        Kind.GForce or Kind.Angle => Math.Round(value * 10).ToString(CultureInfo.InvariantCulture),
        Kind.Heading => Heading(value),
        _ => Math.Round(value).ToString(CultureInfo.InvariantCulture),
    };

    private static string Heading(double value) =>
        (((int)Math.Round(value) % 360 + 360) % 360).ToString("000", CultureInfo.InvariantCulture);
}
