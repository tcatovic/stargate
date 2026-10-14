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
}
