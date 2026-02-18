using System.Threading.Channels;
using Xbim.WexServer.Abstractions.Processing;

namespace Xbim.WexServer.Processing;

/// <summary>
/// In-memory processing queue implementation using System.Threading.Channels.
/// Suitable for single-instance deployments and testing.
/// </summary>
public sealed class ChannelQueue : IProcessingQueue
{
    private readonly Channel<JobEnvelope> _channel;

    public ChannelQueue()
    {
        // Unbounded channel - jobs are queued without back-pressure
        // For production with bounded queues, consider adding configuration
        _channel = Channel.CreateUnbounded<JobEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Creates a ChannelQueue with a bounded capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of jobs in the queue.</param>
    public ChannelQueue(int capacity)
    {
        _channel = Channel.CreateBounded<JobEnvelope>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Completes the queue, signaling no more items will be added.
    /// </summary>
    public void Complete()
    {
        _channel.Writer.Complete();
    }

    /// <summary>
    /// Gets the number of items currently in the queue (for testing/monitoring).
    /// </summary>
    public int Count => _channel.Reader.Count;
}
