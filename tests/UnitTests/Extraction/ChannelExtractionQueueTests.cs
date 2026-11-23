using System.Diagnostics;
using FluentAssertions;
using Klesia.Infrastructure.Extraction;
using Klesia.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnitTests.Extraction;

public sealed class ChannelExtractionQueueTests
{
    [Fact]
    public async Task ReadAllAsync_ReturnsQueuedDemandIdsInOrder()
    {
        var queue = CreateQueue(capacity: 3);

        queue.TryEnqueue(21).Should().BeTrue();
        queue.TryEnqueue(22).Should().BeTrue();

        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Should().Be(21);
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Should().Be(22);
    }

    [Fact]
    public void TryEnqueue_WhenCapacityIsReached_ReturnsFalseWithoutBlockingAndLogsDemandId()
    {
        var logger = new RecordingLogger<ChannelExtractionQueue>();
        var queue = CreateQueue(capacity: 1, logger);
        queue.TryEnqueue(31).Should().BeTrue();
        var stopwatch = Stopwatch.StartNew();

        var accepted = queue.TryEnqueue(32);

        stopwatch.Stop();
        accepted.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("extraction queue full; demand will be recovered by the sweep", StringComparison.Ordinal)
            && entry.Message.Contains("32", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryEnqueue_ConcurrentWritersDoNotLoseAcceptedDemandIds()
    {
        const int demandCount = 128;
        var queue = CreateQueue(capacity: demandCount);

        var accepted = await Task.WhenAll(Enumerable.Range(1, demandCount)
            .Select(demandId => Task.Run(() => queue.TryEnqueue(demandId))));

        accepted.Should().OnlyContain(wasAccepted => wasAccepted);
        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        var demandIds = new List<int>(demandCount);
        for (var index = 0; index < demandCount; index++)
        {
            (await reader.MoveNextAsync()).Should().BeTrue();
            demandIds.Add(reader.Current);
        }

        demandIds.Should().BeEquivalentTo(Enumerable.Range(1, demandCount));
    }

    private static ChannelExtractionQueue CreateQueue(
        int capacity,
        ILogger<ChannelExtractionQueue>? logger = null) =>
        new(Options.Create(new ExtractionOptions { QueueCapacity = capacity }),
            logger ?? new RecordingLogger<ChannelExtractionQueue>());

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}