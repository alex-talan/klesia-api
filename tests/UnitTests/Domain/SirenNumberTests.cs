using Domain.Demands;
using FluentAssertions;

namespace UnitTests.Domain;

public sealed class SirenNumberTests
{
    [Theory]
    [InlineData("732829320", "732-829-320")]
    [InlineData("000000001", "000-000-001")]
    public void ToFormattedString_GroupsDigitsByThree(string value, string expected)
    {
        SirenNumber.Create(value).ToFormattedString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678a")]
    [InlineData("732-829-320")]
    [InlineData("123 56789")]
    [InlineData("١٢٣٤٥٦٧٨٩")]
    public void TryCreate_RejectsAnythingButNineAsciiDigits(string? value)
    {
        SirenNumber.TryCreate(value, out SirenNumber? siren).Should().BeFalse();
        siren.Should().BeNull();
    }

    [Fact]
    public void Create_KeepsLeadingZeros()
    {
        SirenNumber.Create("007000000").Value.Should().Be("007000000");
    }

    [Fact]
    public void Create_ThrowsForInvalidValue()
    {
        Action act = () => SirenNumber.Create("abc");

        act.Should().Throw<ArgumentException>();
    }
}
