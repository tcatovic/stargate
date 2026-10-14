namespace StargateAPI.Business.Validation
{
    /// <summary>Shared bounds for free-text identifiers (person name, rank, duty title). Reused by tasks 002 and 006.</summary>
    public static class InputRules
    {
        public const int MaxLength = 100;
        // Unicode letters (+ combining marks), ASCII digits, space, period, apostrophe (U+0027), hyphen.
        public const string AllowedCharactersPattern = @"^[\p{L}\p{M}0-9 .'-]+$";
        public const string AllowedCharactersMessage =
            "Use only letters, digits, spaces, hyphens, apostrophes and periods (at most 100 characters).";
    }
}
