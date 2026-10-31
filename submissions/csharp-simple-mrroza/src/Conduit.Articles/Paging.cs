using System.Globalization;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles;

public static class Paging
{
    // Parsed by hand: normal binding would accept "+5" and " 5", and treat "limit=" as absent.
    public static bool TryParse(string? limitText, string? offsetText, out int limit, out int offset)
    {
        limit = 20;
        offset = 0;
        return TryParseNumber(limitText, ref limit) && limit is >= 1 and <= 100 && TryParseNumber(offsetText, ref offset);
    }

    static bool TryParseNumber(string? text, ref int value) =>
        text is null || int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public static readonly Error Invalid = Error.Validation("limit must be 1 to 100 and offset 0 or more");
}
