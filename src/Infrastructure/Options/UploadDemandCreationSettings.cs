using Klesia.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Klesia.Infrastructure.Options;

public sealed class UploadDemandCreationSettings(IOptions<UploadOptions> uploadOptions) : IDemandCreationSettings
{
    public long MaxPdfSizeBytes => uploadOptions.Value.MaxPdfSizeBytes;
}