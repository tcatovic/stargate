using System.ComponentModel.DataAnnotations;
using StargateAPI.Business.Data;
using StargateAPI.Business.Validation;

namespace StargateAPI.Tests;

/// <summary>The shared hostile list: every value must be rejected by the ASCII-only name rule, wherever it appears.</summary>
public static class NonAsciiNames
{
    public static readonly string[] All =
    [
        "Zo\u00EB", "Jos\u00E9", "\u00C9mile", "Ren\u00E9e", "Rene\u0301e", "\uFF21da", "\u0410da", "\u0391da", "Ada \U0001F680",
        "\u00A0Ada", "Ada\u00A0", "\u3000Ada", "\u0085Ada", "Ada\u2003", "A\u00A0da", "O\u2019Brien", "\u212Aate",
        "\u017Fam", "\u0663", "\u0E01\u0E34", "Ada\u200B", "Ada\u3164", "Ada\u180E", "\u180EAda", "A\u034Fda", "Ada\uFE0F", "\uFF11",
    ];

    public static TheoryData<string> Data()
    {
        var data = new TheoryData<string>();
        foreach (var value in All)
        {
            data.Add(value);
        }

        return data;
    }
}

public class InputRulesTests
{
    private static readonly RegularExpressionAttribute Charset = new(InputRules.AllowedCharactersPattern);
    private static readonly StringLengthAttribute Length = new(InputRules.MaxLength);

    private static bool IsValid(string value) => Charset.IsValid(value) && Length.IsValid(value);

    [Fact]
    public void R1_EverySeededValueIsValid()
    {
        var values = DevelopmentSeedData.People().SelectMany(p => new[] { p.Name }
            .Concat(p.AstronautDetail is null ? [] : [p.AstronautDetail.CurrentRank, p.AstronautDetail.CurrentDutyTitle])
            .Concat(p.AstronautDuties.SelectMany(d => new[] { d.Rank, d.DutyTitle }))).ToList();

        Assert.NotEmpty(values);
        Assert.All(values, v => Assert.True(IsValid(v), v));
    }

    [Theory]
    [InlineData("Aoife O'Brien")]
    [InlineData("Anne-Marie St. John")]
    [InlineData("2LT")]
    [InlineData("RETIRED")]
    public void R2_ReasonableValuesAreValid(string value) => Assert.True(IsValid(value));

    [Fact]
    public void R2_HundredCharactersIsValid() => Assert.True(IsValid(new string('a', 100)));

    [Fact]
    public void R3_HundredAndOneCharactersIsInvalid()
    {
        var value = new string('a', 101);
        Assert.True(Charset.IsValid(value));
        Assert.False(Length.IsValid(value));
    }

    [Theory]
    [InlineData("x' OR '1'='1")]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    [InlineData("a/b")]
    [InlineData("a;b")]
    [InlineData("<script>")]
    [InlineData("a+b")]
    [InlineData("a&b")]
    [InlineData("\"quoted\"")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\u0000b")]
    [InlineData("O’Brien")]
    [InlineData("abc\n")]
    [InlineData("\nabc")]
    [InlineData("\t")]
    public void R4_DisallowedCharactersAreInvalid(string value) => Assert.False(IsValid(value));

    private static bool IsValidName(string value) => InputRules.IsValidName(InputRules.Normalize(value));

    [Theory]
    [MemberData(nameof(NonAsciiNames.Data), MemberType = typeof(NonAsciiNames))]
    public void R4b_NonAsciiIsInvalid_AndNeverTrimmed(string value)
    {
        Assert.False(IsValidName(value));
        Assert.False(Charset.IsValid(value));
        Assert.False(new ValidNameAttribute().IsValid(value));
    }

    [Theory]
    [InlineData("Aoife O'Brien")]
    [InlineData("Anne-Marie St. John")]
    [InlineData("2LT")]
    [InlineData("RETIRED")]
    [InlineData("O'Brien-Smith Jr.")]
    [InlineData("a.")]
    [InlineData("O'Brien")]
    [InlineData("A-1")]
    [InlineData(". a")]
    [InlineData("7")]
    public void R24_AsciiNamesAreValid(string value) => Assert.True(IsValidName(value));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("'")]
    [InlineData("-")]
    [InlineData(". -")]
    [InlineData(" ' . - ")]
    public void R27_PunctuationOnlyIsInvalid(string value)
    {
        Assert.False(IsValidName(value));
        Assert.False(Charset.IsValid(value));
    }

    [Fact]
    public void R28_AHundredCharacterPunctuationRunWithALateLetter_IsLinear_AndAPunctuationOnlyRunIsRejectedQuickly()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(IsValidName(new string('.', 100)));
        Assert.False(IsValidName(new string('.', 99) + "\u00E9"));
        Assert.True(IsValidName(new string('-', 99) + "a"));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void R24_HundredLettersIsValid() => Assert.True(IsValidName(new string('Z', 100)));

    [Theory]
    [InlineData("\u212Aate")] // KELVIN SIGN: matches [k] only under IgnoreCase
    [InlineData("\u017Fam")] // LATIN SMALL LETTER LONG S: matches [s] only under IgnoreCase
    [InlineData("\u0663")] // ARABIC-INDIC DIGIT THREE: matched by \d
    [InlineData("\uFF11")] // FULLWIDTH DIGIT ONE: matched by \d
    public void R25_NoIgnoreCaseFoldingAndNoUnicodeDigits(string value) => Assert.False(IsValidName(value));

    [Fact]
    public void R26_ValidNameAttribute_HasNoRawBound_AndTrimsBeforeMeasuring()
    {
        var attribute = new ValidNameAttribute();

        Assert.False(attribute.IsValid(new string('a', 5000)));
        Assert.True(attribute.IsValid("   " + new string('a', 100) + "\t"));
        Assert.False(attribute.IsValid(new string(' ', 5000) + new string('a', 101)));
        Assert.True(attribute.IsValid(new string(' ', 5000) + "Ada"));
    }

    [Theory]
    [InlineData("  Ada \t", "Ada")]
    [InlineData("\r\nAda\r\n", "Ada")]
    [InlineData("\v\fAda\f\v", "Ada")]
    [InlineData("Tomas  Lindqvist", "Tomas  Lindqvist")]
    public void R11_NormalizeTrimsSurroundingAsciiWhitespaceOnly(string input, string expected) =>
        Assert.Equal(expected, InputRules.Normalize(input));

    [Theory]
    [InlineData("\u00A0Ada")]
    [InlineData("Ada\u3000")]
    [InlineData("\u0085Ada")]
    [InlineData("Ada\u2003")]
    public void R11_NonAsciiWhitespaceIsNotTrimmed(string input) => Assert.Equal(input, InputRules.Normalize(input));

    [Fact]
    public void R12_LoneSurrogate_DoesNotThrow_AndIsInvalid()
    {
        var result = InputRules.Normalize("\uD800abc");

        Assert.False(InputRules.IsValidName(result));
    }

    [Fact]
    public void R17_ARawTrailingNewlineNeverMatchesThePattern()
    {
        Assert.DoesNotMatch(InputRules.AllowedCharactersPattern, "Ada\n");
        Assert.Matches(InputRules.AllowedCharactersPattern, "Ada");
    }

    [Fact]
    public void R18_LengthIsMeasuredAfterTrimming()
    {
        Assert.True(IsValidName("   " + new string('a', 100) + "   "));
        Assert.False(IsValidName(new string('a', 101)));
        Assert.False(IsValidName(""));
        Assert.False(IsValidName("   "));
    }

    [Fact]
    public void R18b_IsValidName_DoesNotNormaliseOrTrimItself()
    {
        Assert.False(InputRules.IsValidName(""));
        Assert.False(InputRules.IsValidName(new string('a', 101)));
        Assert.True(InputRules.IsValidName(new string('a', 100)));
    }

    [Fact]
    public void R19_ValidNameAttribute()
    {
        var attribute = new ValidNameAttribute();

        Assert.True(attribute.IsValid(null));
        Assert.False(attribute.IsValid(42));
        Assert.True(attribute.IsValid(" Ada "));
        Assert.False(attribute.IsValid("<script>"));
        Assert.Equal(
            "Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods, with at least one letter or digit (at most 100 characters).",
            attribute.FormatErrorMessage("Name"));
        Assert.NotNull(attribute.GetValidationResult("<x>", new ValidationContext(new object()) { MemberName = "Name" }));
    }

    [Fact]
    public void R20_SeedNamesAreValidAndAlreadyNormalised()
    {
        foreach (var person in DevelopmentSeedData.People())
        {
            Assert.True(IsValidName(person.Name), person.Name);
            Assert.Equal(person.Name, InputRules.Normalize(person.Name));
        }
    }
}
