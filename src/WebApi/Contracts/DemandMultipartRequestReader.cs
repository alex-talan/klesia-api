using System.Text;
using Klesia.Application.Demands;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Klesia.WebApi.Contracts;

internal abstract record DemandMultipartReadResult
{
    private DemandMultipartReadResult()
    {
    }

    internal sealed record Success(CreateDemandInput Input) : DemandMultipartReadResult;

    internal sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : DemandMultipartReadResult;

    internal sealed record TooLarge : DemandMultipartReadResult;
}

internal static class DemandMultipartRequestReader
{
    public static async Task<DemandMultipartReadResult> ReadAsync(
        HttpRequest request,
        long maxPdfSizeBytes,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("bae_file", "The request must use multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return Invalid("bae_file", "The multipart boundary is missing.");
        }

        var requestLimit = UploadRequestLimits.ForPdfSize(maxPdfSizeBytes);
        if (request.ContentLength > requestLimit)
        {
            return new DemandMultipartReadResult.TooLarge();
        }

        var reader = new MultipartReader(boundary, request.Body)
        {
            BodyLengthLimit = requestLimit
        };
        string? gkNumber = null;
        string? fileContentType = null;
        MemoryStream? pdfContent = null;
        var transferred = false;

        try
        {
            while (true)
            {
                MultipartSection? section;
                try
                {
                    section = await reader.ReadNextSectionAsync(cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new InvalidDataException("The multipart request is malformed.", exception);
                }

                if (section is null)
                {
                    break;
                }

                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid("bae_file", "A multipart part is malformed.");
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (string.IsNullOrEmpty(name))
                {
                    return Invalid("bae_file", "A multipart part is missing its name.");
                }

                var hasFileName = disposition.FileName.HasValue || disposition.FileNameStar.HasValue;
                if (string.Equals(name, "gk_number", StringComparison.Ordinal))
                {
                    if (gkNumber is not null)
                    {
                        return Invalid("gk_number", "The gk_number part must appear exactly once.");
                    }

                    if (hasFileName)
                    {
                        return Invalid("gk_number", "The gk_number part must be text.");
                    }

                    gkNumber = await ReadGkNumberAsync(section.Body, cancellationToken);
                    continue;
                }

                if (string.Equals(name, "bae_file", StringComparison.Ordinal))
                {
                    if (pdfContent is not null)
                    {
                        return Invalid("bae_file", "The bae_file part must appear exactly once.");
                    }

                    if (!hasFileName)
                    {
                        return Invalid("bae_file", "The bae_file part must be a file.");
                    }

                    pdfContent = new MemoryStream();
                    if (!await CopyPdfAsync(section.Body, pdfContent, maxPdfSizeBytes, cancellationToken))
                    {
                        return new DemandMultipartReadResult.TooLarge();
                    }

                    pdfContent.Position = 0;
                    fileContentType = section.Headers is not null
                        && section.Headers.TryGetValue(HeaderNames.ContentType, out var values)
                        ? values.ToString()
                        : null;
                    continue;
                }

                return Invalid(SafeErrorKey(name), "The multipart part is not supported.");
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (gkNumber is null)
            {
                errors["gk_number"] = ["The gk_number part is required."];
            }

            if (pdfContent is null)
            {
                errors["bae_file"] = ["The bae_file part is required."];
            }

            if (errors.Count > 0)
            {
                return new DemandMultipartReadResult.Invalid(errors);
            }

            transferred = true;
            return new DemandMultipartReadResult.Success(new CreateDemandInput(
                gkNumber!,
                fileContentType,
                pdfContent!.Length,
                pdfContent));
        }
        finally
        {
            if (!transferred)
            {
                pdfContent?.Dispose();
            }
        }
    }

    private static async Task<string> ReadGkNumberAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[256];
        var tooLong = false;
        int read;
        while ((read = await content.ReadAsync(bytes, 0, bytes.Length, cancellationToken)) > 0)
        {
            if (!tooLong)
            {
                var remaining = 19 - (int)buffer.Length;
                var count = Math.Min(remaining, read);
                if (count > 0)
                {
                    buffer.Write(bytes, 0, count);
                }

                tooLong = buffer.Length > 18 || read > count;
            }
        }

        return tooLong ? "invalid" : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task<bool> CopyPdfAsync(
        Stream source,
        Stream destination,
        long maxPdfSizeBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var tooLarge = false;
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            if (tooLarge)
            {
                continue;
            }

            var remaining = maxPdfSizeBytes - destination.Length;
            if (read > remaining)
            {
                if (remaining > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, (int)remaining), cancellationToken);
                }

                tooLarge = true;
            }
            else
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        return !tooLarge;
    }

    private static string SafeErrorKey(string name) =>
        name.Length <= 64 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? name
            : "part";

    private static DemandMultipartReadResult.Invalid Invalid(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [key] = [message]
        });
}