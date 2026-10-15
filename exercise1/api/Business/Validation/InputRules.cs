using System.Text.RegularExpressions;

namespace StargateAPI.Business.Validation
{
    /// <summary>
    /// Shared bounds for free-text identifiers (person name, rank, duty title), defined once for every endpoint.
    /// Names are ASCII-only: <see cref="Normalize"/> trims ASCII whitespace, then a value must be 1 to
    /// <see cref="MaxLength"/> characters of A-Z, a-z, 0-9, space, period, apostrophe and hyphen, with at least one
    /// ASCII letter or digit (so ".", ".." and "'" are not names; ".." would also break URL building).
    /// No Unicode normalisation: any non-ASCII character, including non-ASCII whitespace at an edge, is invalid.
    /// </summary>
    public static class InputRules
    {
        public const int MaxLength = 100;

        // Written out (never \d or \p{L}, which match non-ASCII digits and letters) and used WITHOUT
        // RegexOptions.IgnoreCase (under which U+212A KELVIN SIGN matches k). \z, not $, so a trailing newline never matches.
        // The lookahead scans only the allowed punctuation up to the first letter or digit, so matching stays linear.
        public const string AllowedCharactersPattern = @"^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z";

        public const string AllowedCharactersMessage =
            "Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods, with at least one letter or digit (at most 100 characters).";

        /// <summary>Earliest and latest allowed duty start date (inclusive). A lower bound is needed anyway: start minus one day must not underflow.</summary>
        public static readonly DateTime MinDutyDate = new(1900, 1, 1);

        public static readonly DateTime MaxDutyDate = new(2100, 12, 31);

        public const string DutyDateMessage = "Use a date between 1900-01-01 and 2100-12-31.";

        // Tab, LF, VT, FF, CR, space. Deliberately not string.Trim()'s Unicode set: a non-ASCII character is never
        // silently removed, so it reaches IsValidName and is rejected.
        private static readonly char[] AsciiWhitespace = ['\t', '\n', '\v', '\f', '\r', ' '];

        private static readonly Regex AllowedCharacters = new(
            AllowedCharactersPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        /// <summary>Removes leading and trailing ASCII whitespace. Never throws.</summary>
        public static string Normalize(string value) => value.Trim(AsciiWhitespace);

        /// <summary>True when the date part of <paramref name="value"/> is within <see cref="MinDutyDate"/> and <see cref="MaxDutyDate"/>.</summary>
        public static bool IsValidDutyDate(DateTime value) => value.Date >= MinDutyDate && value.Date <= MaxDutyDate;

        /// <summary>True when the value has 1 to <see cref="MaxLength"/> characters and matches the pattern. Expects the output of <see cref="Normalize"/>.</summary>
        public static bool IsValidName(string normalized)
        {
            if (normalized.Length is < 1 or > MaxLength)
            {
                return false;
            }

            try
            {
                return AllowedCharacters.IsMatch(normalized);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
