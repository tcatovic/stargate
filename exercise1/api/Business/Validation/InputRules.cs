using System.Text;
using System.Text.RegularExpressions;

namespace StargateAPI.Business.Validation
{
    /// <summary>
    /// Shared bounds for free-text identifiers (person name, rank, duty title). The name rule is defined once here
    /// for tasks 002 and 006: a value is NFC-normalised and trimmed (<see cref="Normalize"/>), then must be
    /// 1 to <see cref="MaxLength"/> UTF-16 code units of letters (each followed by at most 3 combining marks),
    /// ASCII digits, space, period, apostrophe and hyphen, with no invisible code points.
    /// </summary>
    public static class InputRules
    {
        public const int MaxLength = 100;

        // Default-ignorable code points that Unicode classes as letters (Lo) or nonspacing marks (Mn): they render as
        // nothing, so two visually identical names could differ. U+034F, U+115F, U+1160, U+17B4, U+17B5, U+180B-U+180F,
        // U+3164, U+FE00-U+FE0F, U+FFA0. Written as regex escapes so the source stays readable and reviewable.
        private const string Invisible = @"\u034F\u115F\u1160\u17B4\u17B5\u180B-\u180F\u3164\uFE00-\uFE0F\uFFA0";

        // A letter followed by up to 3 combining marks, or one of the ASCII characters. \z (not $) so that a trailing
        // newline can never match, even for code that calls Regex.IsMatch directly.
        public const string AllowedCharactersPattern =
            @"^(?:(?![" + Invisible + @"])\p{L}(?:(?![" + Invisible + @"])\p{M}){0,3}|[0-9 .'-])+\z";

        public const string AllowedCharactersMessage =
            "Use only letters, digits, spaces, hyphens, apostrophes and periods (at most 100 characters).";

        private static readonly Regex AllowedCharacters = new(
            AllowedCharactersPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        /// <summary>
        /// NFC, then <see cref="string.Trim()"/>. Never throws: ill-formed UTF-16 (for example a lone surrogate) makes
        /// <see cref="string.Normalize()"/> throw, in which case the trimmed value is returned unnormalised and then
        /// fails <see cref="IsValidName"/>.
        /// </summary>
        public static string Normalize(string value)
        {
            try
            {
                return value.Normalize(NormalizationForm.FormC).Trim();
            }
            catch (ArgumentException)
            {
                return value.Trim();
            }
        }

        /// <summary>True when the value has 1 to <see cref="MaxLength"/> characters and matches the pattern. Expects the output of <see cref="Normalize"/>; does not normalise again.</summary>
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
