using Klesia.Domain.Demands;

namespace Klesia.Application.Demands;

public sealed record CreateDemandValidationInput(
    string GkNumber,
    string? DeclaredContentType,
    long Length,
    Stream Content);

public abstract record CreateDemandValidationResult
{
    private CreateDemandValidationResult()
    {
    }

    public sealed record Valid(Stream PdfContent) : CreateDemandValidationResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateDemandValidationResult;

    public sealed record TooLarge : CreateDemandValidationResult;

    public sealed record Unprocessable(IReadOnlyDictionary<string, string[]> Errors) : CreateDemandValidationResult;
}

public sealed class CreateDemandValidator
{
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];

    public async Task<CreateDemandValidationResult> ValidateAsync(
        CreateDemandValidationInput input,
        long maxPdfSizeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Content);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPdfSizeBytes, 0);

        if (!GKNumber.TryCreate(input.GkNumber, out _))
        {
            return Invalid("gk_number", "The gk_number format is invalid.");
        }

        if (input.Length > maxPdfSizeBytes)
        {
            return new CreateDemandValidationResult.TooLarge();
        }

        if (input.Length <= 0 || !IsPdfContentType(input.DeclaredContentType))
        {
            return Unprocessable("bae_file", "The uploaded file is not a valid PDF.");
        }

        if (input.Content.CanSeek)
        {
            input.Content.Position = 0;
        }

        var signature = new byte[PdfSignature.Length];
        var bytesRead = 0;
        while (bytesRead < signature.Length)
        {
            var count = await input.Content.ReadAsync(signature.AsMemory(bytesRead), cancellationToken);
            if (count == 0)
            {
                break;
            }

            bytesRead += count;
        }

        if (bytesRead != PdfSignature.Length || !signature.AsSpan().SequenceEqual(PdfSignature))
        {
            if (input.Content.CanSeek)
            {
                input.Content.Position = 0;
            }

            return Unprocessable("bae_file", "The uploaded file is not a valid PDF.");
        }

        var pdfContent = input.Content.CanSeek
            ? Rewind(input.Content)
            : new PrefixReplayStream(signature, input.Content);

        return new CreateDemandValidationResult.Valid(pdfContent);
    }

    private static bool IsPdfContentType(string? contentType) =>
        string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    private static CreateDemandValidationResult.Invalid Invalid(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [key] = [message]
        });

    private static CreateDemandValidationResult.Unprocessable Unprocessable(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [key] = [message]
        });

    private static Stream Rewind(Stream stream)
    {
        stream.Position = 0;
        return stream;
    }

    private sealed class PrefixReplayStream(byte[] prefix, Stream innerStream) : Stream
    {
        private int _prefixOffset;

        public override bool CanRead => innerStream.CanRead;

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

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var prefixCount = CopyPrefix(buffer);
            return prefixCount > 0 ? prefixCount : innerStream.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var prefixCount = CopyPrefix(buffer.Span);
            return prefixCount > 0
                ? prefixCount
                : await innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int CopyPrefix(Span<byte> buffer)
        {
            var remaining = prefix.Length - _prefixOffset;
            if (remaining == 0 || buffer.IsEmpty)
            {
                return 0;
            }

            var count = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(_prefixOffset, count).CopyTo(buffer);
            _prefixOffset += count;
            return count;
        }
    }
}