using System.Text;
using System.Text.RegularExpressions;

namespace WarThunder;

/// <summary>
/// Multi-line key text with placeholders, e.g. "IAS {ias}\nALT {alt:ft}" or "{aircraft}".
/// {field} uses the field's default unit, {field:unit} picks one (kt, ft, fpm, ...).
/// A literal "\n" typed in the settings box becomes a line break.
/// </summary>
public sealed partial class TextTemplate
{
    private readonly List<object> _parts = []; // string literal or Token

    private sealed record Token(Field? Field, string Unit, bool Aircraft);

    public TextTemplate(string template)
    {
        template = template.Replace("\\n", "\n");
        var last = 0;
        foreach (Match m in Placeholder().Matches(template))
        {
            _parts.Add(template[last..m.Index]);
            last = m.Index + m.Length;
            var id = m.Groups[1].Value.ToLowerInvariant();
            if (id == "aircraft")
            {
                _parts.Add(new Token(null, "", true));
                continue;
            }
            var field = Field.All.FirstOrDefault(f => f.Id == id);
            if (field is null)
            {
                _parts.Add(m.Value); // Unknown placeholder: show as typed so the mistake is visible.
                continue;
            }
            var units = Units.For(field.Kind);
            var wanted = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : null;
            _parts.Add(new Token(field, wanted is not null && units.Contains(wanted) ? wanted : units[0], false));
        }
        _parts.Add(template[last..]);
    }

    public bool NeedsState => _parts.OfType<Token>().Any(t => t.Field?.Source == Source.State);

    public bool NeedsIndicators => _parts.OfType<Token>().Any(t => t.Aircraft || t.Field?.Source == Source.Indicators);

    public string Render(Telemetry? t)
    {
        var sb = new StringBuilder();
        foreach (var part in _parts)
        {
            if (part is string s)
            {
                sb.Append(s);
                continue;
            }
            var token = (Token)part;
            if (token.Aircraft)
            {
                sb.Append(t?.Text("type") is { } type ? PrettyAircraft(type) : "--");
                continue;
            }
            var raw = t is { Valid: true } ? token.Field!.Read(t) : null;
            sb.Append(raw is { } v ? Units.ForTitle(Units.Convert(v, token.Unit), token.Field!.Kind) : "--");
        }
        return sb.ToString();
    }

    /// <summary>"ffvs_j22_b" -> "FFVS J22 B". Good enough for a key face.</summary>
    private static string PrettyAircraft(string type) => type.Replace('_', ' ').ToUpperInvariant();

    [GeneratedRegex(@"\{(\w+)(?::(\w+))?\}")]
    private static partial Regex Placeholder();
}
