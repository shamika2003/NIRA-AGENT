/*
 * filename: AgentResponseDispatcher.cs
 */

using System.Threading.Channels;

namespace SegaAgent.Agent;

public sealed class AgentResponseDispatcher : IDisposable
{
    private readonly Channel<AgentResponse> _channel =
        Channel.CreateUnbounded<AgentResponse>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    private readonly CancellationTokenSource _shutdown =
        new();

    private bool _disposed;


    public async ValueTask PublishAsync(
        AgentResponse response,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _channel.Writer.WriteAsync(
            response,
            cancellationToken);
    }


    public IAsyncEnumerable<AgentResponse> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return ReadInternalAsync(
            cancellationToken);
    }


    private async IAsyncEnumerable<AgentResponse>
        ReadInternalAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
    {
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                _shutdown.Token,
                cancellationToken);

        await foreach (
            var response
            in _channel.Reader.ReadAllAsync(
                linked.Token))
        {
            yield return response;
        }
    }


    public void Complete()
    {
        _channel.Writer.TryComplete();
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _shutdown.Cancel();

        _channel.Writer.TryComplete();

        _shutdown.Dispose();
    }
}


// =============================================================
// RESPONSE
// =============================================================

public sealed record AgentResponse(
    AgentRequestSource Source,
    AgentStreamChunk Chunk
);