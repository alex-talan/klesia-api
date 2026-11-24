namespace Klesia.WebApi;

internal static class UploadRequestLimits
{
    public const long MultipartOverheadBytes = 64 * 1024;

    public static long ForPdfSize(long maxPdfSizeBytes) => checked(maxPdfSizeBytes + MultipartOverheadBytes);
}