using System.Threading.Channels;
using Xbim.WexServer.Abstractions.Processing;

namespace Xbim.WexServer.Tests.Endpoints;

/// <summary>
/// In-memory processing queue implementation for testing.
/// </summary>
public class TestInMemoryProcessingQueue : IProcessingQueue
{
    private readonly Channel<JobEnvelope> _channel = Channel.CreateUnbounded<JobEnvelope>();

    public ValueTask EnqueueAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    public ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken)!;
    }
}
