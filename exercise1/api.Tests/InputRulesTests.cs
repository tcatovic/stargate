using System.ComponentModel.DataAnnotations;
using StargateAPI.Business.Data;
using StargateAPI.Business.Validation;

namespace StargateAPI.Tests;

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
    [InlineData("Zoë")]
    [InlineData("José")]
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

    [Fact]
    public void R10_NfdIsNormalisedToNfc_AndValid()
    {
        var nfd = "Rene\u0301e";
        Assert.Equal(6, nfd.Length); // the NFD form really is longer than the NFC form (5)

        var result = InputRules.Normalize(nfd);

        Assert.Equal("Ren\u00E9e", result);
        Assert.Equal(5, result.Length);
        Assert.True(InputRules.IsValidName(result));
    }

    [Theory]
    [InlineData("  Ada \t", "Ada")]
    [InlineData("\r\nAda\r\n", "Ada")]
    [InlineData("Tomas  Lindqvist", "Tomas  Lindqvist")]
    public void R11_NormalizeTrimsSurroundingWhitespaceOnly(string input, string expected) =>
        Assert.Equal(expected, InputRules.Normalize(input));

    [Fact]
    public void R12_LoneSurrogate_DoesNotThrow_AndIsInvalid()
    {
        var result = InputRules.Normalize("\uD800abc");

        Assert.False(InputRules.IsValidName(result));
    }

    [Theory]
    [InlineData("Ada\u3164")]
    [InlineData("\u3164")]
    [InlineData("\u115F\u1160")]
    [InlineData("Ada\uFFA0")]
    [InlineData("A\u034Fda")]
    [InlineData("Ada\uFE0F")]
    [InlineData("Ada\uFE00")]
    [InlineData("Ada\uFE01")]
    [InlineData("Ada\u17B4")]
    [InlineData("Ada\u17B5")]
    [InlineData("Ada\u180B")]
    [InlineData("Ada\u180C")]
    [InlineData("Ada\u180D")]
    [InlineData("A\u180Eda")]
    [InlineData("Ada\u180F")]
    [InlineData("Ada\u200B")]
    public void R13_InvisibleCodePointsAreInvalid(string value) => Assert.False(IsValidName(value));

    [Fact]
    public void R13c_EveryCodePointOfTheInvisibleSetIsRejectedInsideAName()
    {
        // Written as numbers, independent of the pattern: 034F, 115F, 1160, 17B4, 17B5, 180B-180F, 3164, FE00-FE0F, FFA0.
        var invisible = new List<int> { 0x034F, 0x115F, 0x1160, 0x17B4, 0x17B5, 0x3164, 0xFFA0 };
        invisible.AddRange(Enumerable.Range(0x180B, 5));
        invisible.AddRange(Enumerable.Range(0xFE00, 16));

        Assert.Equal(28, invisible.Count);
        foreach (var codePoint in invisible)
        {
            var name = "Ad" + (char)codePoint + "a";
            Assert.False(IsValidName(name), $"U+{codePoint:X4} must be rejected");
        }
        Assert.True(IsValidName("Ada"));
    }

    [Theory]
    [InlineData("\u3164A")]
    [InlineData("A\u3164")]
    [InlineData("\u115F")]
    [InlineData("\u1160")]
    [InlineData("\uFFA0")]
    public void R13b_InvisibleLettersAreInvalidEvenAtTheEdges_AndWithoutTrimmingHelp(string value) =>
        Assert.DoesNotMatch(InputRules.AllowedCharactersPattern, value);

    [Theory]
    [InlineData("\u0301Ada")]
    [InlineData("1\u0301")]
    [InlineData("Ada \u0301")]
    [InlineData("O'\u0301")]
    [InlineData("-\u0301")]
    public void R14_AMarkThatDoesNotFollowALetterIsInvalid(string value) => Assert.False(IsValidName(value));

    [Fact]
    public void R15_ThreeMarksAreValidFourAreNot_MeasuredAfterNfc()
    {
        // q has no precomposed form, so NFC keeps every mark.
        Assert.True(IsValidName("q\u0301\u0302\u0303"));
        Assert.False(IsValidName("q\u0301\u0302\u0303\u0304"));
        // NFC composes a + U+0301 into a single letter, leaving three marks.
        Assert.True(IsValidName("a\u0301\u0302\u0303\u0304"));
    }

    [Theory]
    [InlineData("\u0E01\u0E34\u0E48")] // Thai
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947")] // Devanagari
    [InlineData("Zo\u00EB")]
    [InlineData("O'Brien-Smith Jr.")]
    [InlineData("Jos\u00E9")]
    public void R16_RealNamesInSeveralScriptsAreValid(string value) => Assert.True(IsValidName(value));

    [Fact]
    public void R17_ARawTrailingNewlineNeverMatchesThePattern()
    {
        Assert.DoesNotMatch(InputRules.AllowedCharactersPattern, "Ada\n");
        Assert.Matches(InputRules.AllowedCharactersPattern, "Ada");
    }

    [Fact]
    public void R18_LengthIsMeasuredAfterTrimmingAndNormalising()
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
        Assert.True(attribute.IsValid("Rene\u0301e"));
        Assert.False(attribute.IsValid("<script>"));
        Assert.False(attribute.IsValid("Ada\u3164"));
        Assert.Equal(
            "Use only letters, digits, spaces, hyphens, apostrophes and periods (at most 100 characters).",
            attribute.FormatErrorMessage("Name"));
        Assert.NotNull(attribute.GetValidationResult("<x>", new ValidationContext(new object()) { MemberName = "Name" }));
    }

    [Fact]
    public void R20_ExistingValidAndInvalidCasesKeepTheirOutcome_ForAllSeedNames()
    {
        foreach (var person in DevelopmentSeedData.People())
        {
            Assert.True(IsValidName(person.Name), person.Name);
            Assert.Equal(person.Name, InputRules.Normalize(person.Name));
        }
    }

    [Fact]
    public void R21_NormalizeTrimsUnicodeWhitespace_NbspAndEmSpace()
    {
        Assert.Equal("Ada", InputRules.Normalize("\u00A0Ada\u2003"));
        Assert.Equal("Ada", InputRules.Normalize("\u3000Ada\u0085"));
    }

    [Fact]
    public void R22_SupplementaryPlaneLettersAreRejectedByDesignToday()
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A is a letter, but \p{L} sees only its two surrogate halves, so any
        // supplementary-plane letter is rejected until a rule decision says otherwise (same as on main).
        Assert.False(IsValidName("\uD835\uDC00"));
        Assert.False(IsValidName("Ada\uD835\uDC00"));
    }

    [Fact]
    public void R23_AttributeRejectsRawInputOverTheRawBound_BeforeNormalising()
    {
        var attribute = new ValidNameAttribute();

        Assert.False(attribute.IsValid(new string('a', InputRules.MaxRawLength + 1)));
        Assert.False(attribute.IsValid(new string(' ', InputRules.MaxRawLength + 1) + "Ada"));
        // 100 NFC-composed letters written decomposed (200 raw characters) are still a valid 100-character name.
        Assert.True(attribute.IsValid(string.Concat(Enumerable.Repeat("e\u0301", 100))));
        Assert.False(attribute.IsValid(string.Concat(Enumerable.Repeat("e\u0301", 101))));
        Assert.Equal(800, InputRules.MaxRawLength);
    }
}
