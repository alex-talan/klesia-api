using System.Text;
using FluentAssertions;
using Klesia.Application.Demands;

namespace UnitTests.Demands;

public sealed class CreateDemandValidatorTests
{
    private const string ValidGkNumber = "GK260902-144743112";
    private const string PdfContentType = "application/pdf";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nPDF content");
    private readonly CreateDemandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_ValidInput_ReturnsRewoundPdfContent()
    {
        using var content = new MemoryStream(PdfBytes);
        var input = new CreateDemandValidationInput(
            ValidGkNumber,
            "APPLICATION/PDF",
            PdfBytes.LongLength,
            content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        var valid = result.Should().BeOfType<CreateDemandValidationResult.Valid>().Subject;
        valid.PdfContent.Should().BeSameAs(content);
        using var copiedContent = new MemoryStream();
        await valid.PdfContent.CopyToAsync(copiedContent);
        copiedContent.ToArray().Should().Equal(PdfBytes);
    }

    [Theory]
    [InlineData("gk260902-144743112")]
    [InlineData("GX260902-144743112")]
    [InlineData("GK26A902-144743112")]
    [InlineData("GK260902144743112")]
    [InlineData("GK260902-14474311")]
    [InlineData(" GK260902-144743112")]
    [InlineData("GK260902-144743112 ")]
    public async Task ValidateAsync_InvalidGk_ReturnsSafeGkError(string gkNumber)
    {
        using var content = new MemoryStream(PdfBytes);
        var input = new CreateDemandValidationInput(gkNumber, PdfContentType, PdfBytes.LongLength, content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        var invalid = result.Should().BeOfType<CreateDemandValidationResult.Invalid>().Subject;
        invalid.Errors.Should().ContainKey("gk_number");
        invalid.Errors["gk_number"].Should().ContainSingle()
            .Which.Should().NotContain(gkNumber);
        content.Position.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_SizeAtLimitIsValid()
    {
        using var content = new MemoryStream(PdfBytes);
        var input = new CreateDemandValidationInput(ValidGkNumber, PdfContentType, PdfBytes.LongLength, content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        result.Should().BeOfType<CreateDemandValidationResult.Valid>();
    }

    [Fact]
    public async Task ValidateAsync_SizeAboveLimitReturnsTooLarge()
    {
        using var content = new MemoryStream(PdfBytes);
        var input = new CreateDemandValidationInput(ValidGkNumber, PdfContentType, PdfBytes.LongLength, content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength - 1, CancellationToken.None);

        result.Should().BeOfType<CreateDemandValidationResult.TooLarge>();
        content.Position.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_EmptyFileReturnsBaeFileError()
    {
        using var content = new MemoryStream([]);
        var input = new CreateDemandValidationInput(ValidGkNumber, PdfContentType, 0, content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        AssertSafeBaeFileError(result);
        content.Position.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_WrongSignatureReturnsBaeFileError()
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes("not a pdf"));
        var input = new CreateDemandValidationInput(ValidGkNumber, PdfContentType, content.Length, content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        AssertSafeBaeFileError(result);
        content.Position.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_WrongDeclaredContentTypeReturnsBaeFileError()
    {
        using var content = new MemoryStream(PdfBytes);
        var input = new CreateDemandValidationInput(
            ValidGkNumber,
            "text/plain",
            PdfBytes.LongLength,
            content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        AssertSafeBaeFileError(result);
        content.Position.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_NonSeekablePdfPreservesSignatureForUpload()
    {
        using var content = new NonSeekableStream(PdfBytes);
        var input = new CreateDemandValidationInput(
            ValidGkNumber,
            PdfContentType,
            PdfBytes.LongLength,
            content);

        var result = await _validator.ValidateAsync(input, PdfBytes.LongLength, CancellationToken.None);

        var valid = result.Should().BeOfType<CreateDemandValidationResult.Valid>().Subject;
        valid.PdfContent.Should().NotBeSameAs(content);
        using var copiedContent = new MemoryStream();
        await valid.PdfContent.CopyToAsync(copiedContent);
        copiedContent.ToArray().Should().Equal(PdfBytes);
    }

    private static void AssertSafeBaeFileError(CreateDemandValidationResult result)
    {
        var unprocessable = result.Should().BeOfType<CreateDemandValidationResult.Unprocessable>().Subject;
        unprocessable.Errors.Should().ContainKey("bae_file");
        unprocessable.Errors["bae_file"].Should().ContainSingle()
            .Which.Should().NotContain(ValidGkNumber);
    }

    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream _innerStream = new(content, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _innerStream.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _innerStream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}