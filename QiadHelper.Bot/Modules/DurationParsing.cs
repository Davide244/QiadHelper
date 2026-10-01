using System.Globalization;

namespace QiadHelper.Bot.Modules;

internal static class DurationParsing
{
    /// <summary>
    ///     Parses staff-entered durations: "30m", "12h", "7d", "4w", "3mo" — or "perm" /
    ///     "permanent" / "5ever" / empty for no expiry. Returns false on garbage input.
    /// </summary>
    public static bool TryParse(string? input, out TimeSpan? duration)
    {
        duration = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        input = input.Trim().ToLowerInvariant();
        if (input is "perm" or "permanent" or "5ever" or "forever" or "never")
        {
            return true;
        }

        var (suffix, factor) = input switch
        {
            _ when input.EndsWith("mo", StringComparison.Ordinal) => ("mo", TimeSpan.FromDays(30)),
            _ when input.EndsWith('w')                            => ("w", TimeSpan.FromDays(7)),
            _ when input.EndsWith('d')                            => ("d", TimeSpan.FromDays(1)),
            _ when input.EndsWith('h')                            => ("h", TimeSpan.FromHours(1)),
            _ when input.EndsWith('m')                            => ("m", TimeSpan.FromMinutes(1)),
            _                                                     => ("", TimeSpan.Zero)
        };

        if (suffix.Length == 0)
        {
            return false;
        }

        var numberPart = input[..^suffix.Length];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || value > 10000)
        {
            return false;
        }

        duration = factor * value;

        return true;
    }

    public static string Describe(TimeSpan? duration) =>
        duration == null ? "permanent" : $"{duration.Value.TotalDays:0.#} day(s)";
}
