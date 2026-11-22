using FluentAssertions;
using Klesia.Domain.Demands;

namespace UnitTests.Domain;

public sealed class DomainValueObjectTests
{
    [Theory]
    [InlineData("GK260902-144743112")]
    public void TryCreate_WithValidGkNumber_ReturnsValueUnchanged(string value)
    {
        var created = GKNumber.TryCreate(value, out var number);

        created.Should().BeTrue();
        number.Should().NotBeNull();
        number!.Value.Should().Be(value);
        number.ToString().Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gk260902-144743112")]
    [InlineData("GX260902-144743112")]
    [InlineData("GK26A902-144743112")]
    [InlineData("GK260902144743112")]
    [InlineData("GK260902-14474311")]
    [InlineData("GK260902-1447431120")]
    [InlineData(" GK260902-144743112")]
    [InlineData("GK260902-144743112 ")]
    [InlineData("GK260 02-144743112")]
    public void TryCreate_WithInvalidGkNumber_ReturnsFalseAndNull(string? value)
    {
        var created = GKNumber.TryCreate(value, out var number);

        created.Should().BeFalse();
        number.Should().BeNull();
    }

    [Fact]
    public void Create_WithInvalidGkNumber_ThrowsArgumentException()
    {
        var act = () => GKNumber.Create("gk260902-144743112");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("ok")]
    [InlineData("fail")]
    public void FromCode_WithKnownCode_RoundTrips(string code)
    {
        var status = DemandStatus.FromCode(code);

        status.Code.Should().Be(code);
        DemandStatus.TryFromCode(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(status);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("done")]
    [InlineData("")]
    [InlineData(null)]
    public void FromCode_WithUnknownCode_RejectsIt(string? code)
    {
        DemandStatus.TryFromCode(code, out var status).Should().BeFalse();
        status.Should().BeNull();

        var act = () => DemandStatus.FromCode(code);

        act.Should().Throw<ArgumentException>();
    }
}