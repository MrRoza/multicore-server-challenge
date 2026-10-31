using System.Globalization;

namespace Conduit.Articles.Abstractions;

/// <summary>A value object: a valid limit and offset.</summary>
public readonly record struct Page(int Limit, int Offset)
{
    public const string InvalidMessage = "limit must be 1 to 100 and offset 0 or more";

    // Parsed by hand: normal binding would accept "+5" and " 5", and treat "limit=" as absent.
    public static bool TryParse(string? limitText, string? offsetText, out Page page)
    {
        var (limit, offset) = (20, 0);
        var valid = TryParseNumber(limitText, ref limit) && limit is >= 1 and <= 100 && TryParseNumber(offsetText, ref offset);
        page = new Page(limit, offset);
        return valid;
    }

    static bool TryParseNumber(string? text, ref int value) =>
        text is null || int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
