using System.Threading.Channels;
using Klesia.Application.Abstractions;
using Klesia.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klesia.Infrastructure.Extraction;

internal sealed class ChannelExtractionQueue : IExtractionQueue, IExtractionQueueReader
{
    private readonly Channel<int> _channel;
    private readonly ILogger<ChannelExtractionQueue> _logger;

    public ChannelExtractionQueue(
        IOptions<ExtractionOptions> options,
        ILogger<ChannelExtractionQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.QueueCapacity, 1);

        _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _logger = logger;
    }

    public bool TryEnqueue(int demandId)
    {
        if (_channel.Writer.TryWrite(demandId))
        {
            return true;
        }

        _logger.LogWarning(
            "extraction queue full; demand will be recovered by the sweep. DemandId: {DemandId}",
            demandId);
        return false;
    }

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}