using System.Text.RegularExpressions;

namespace StargateAPI.Business.Validation
{
    /// <summary>
    /// Shared bounds for free-text identifiers (person name, rank, duty title), defined once for every endpoint.
    /// Names are ASCII-only: <see cref="Normalize"/> trims ASCII whitespace, then a value must be 1 to
    /// <see cref="MaxLength"/> characters of A-Z, a-z, 0-9, space, period, apostrophe and hyphen.
    /// No Unicode normalisation: any non-ASCII character, including non-ASCII whitespace at an edge, is invalid.
    /// </summary>
    public static class InputRules
    {
        public const int MaxLength = 100;

        // Written out (never \d or \p{L}, which match non-ASCII digits and letters) and used WITHOUT
        // RegexOptions.IgnoreCase (under which U+212A KELVIN SIGN matches k). \z, not $, so a trailing newline never matches.
        public const string AllowedCharactersPattern = @"^[A-Za-z0-9 .'-]+\z";

        public const string AllowedCharactersMessage =
            "Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods (at most 100 characters).";

        // Tab, LF, VT, FF, CR, space. Deliberately not string.Trim()'s Unicode set: a non-ASCII character is never
        // silently removed, so it reaches IsValidName and is rejected.
        private static readonly char[] AsciiWhitespace = ['\t', '\n', '\v', '\f', '\r', ' '];

        private static readonly Regex AllowedCharacters = new(
            AllowedCharactersPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        /// <summary>Removes leading and trailing ASCII whitespace. Never throws.</summary>
        public static string Normalize(string value) => value.Trim(AsciiWhitespace);

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
