using FluentAssertions;
using Klesia.Infrastructure.Documents;

namespace UnitTests.Documents;

public sealed class BlobDocumentLocationTests
{
    private static readonly Uri BlobServiceUri = new("https://klesiatest.blob.core.windows.net/");
    private const string ContainerName = "bae";

    [Fact]
    public void CreateBlobName_UsesOwnerAndGuidPdfPath()
    {
        var ownerOid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var blobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var blobName = BlobDocumentLocation.CreateBlobName(ownerOid, blobId);

        blobName.Should().Be("11111111-2222-3333-4444-555555555555/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.pdf");
    }

    [Theory]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/file.pdf", true)]
    [InlineData("https://other.blob.core.windows.net/bae/11111111/file.pdf", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/other/11111111/file.pdf", false)]
    [InlineData("http://klesiatest.blob.core.windows.net/bae/11111111/file.pdf", false)]
    [InlineData("https://user@klesiatest.blob.core.windows.net/bae/11111111/file.pdf", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/file.pdf?sig=secret", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/file.pdf?", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/file.pdf#fragment", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/file.pdf#", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/../file.pdf", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/11111111/%2e%2e/file.pdf", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae/", false)]
    [InlineData("https://klesiatest.blob.core.windows.net/bae//file.pdf", false)]
    public void IsOwnedLocation_OnlyAcceptsHttpsBlobInConfiguredAccountAndContainer(string value, bool expected)
    {
        var result = BlobDocumentLocation.IsOwnedLocation(new Uri(value), BlobServiceUri, ContainerName);

        result.Should().Be(expected);
    }
}