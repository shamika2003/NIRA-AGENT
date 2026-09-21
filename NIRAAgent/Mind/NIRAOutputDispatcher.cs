/*
 * filename: NIRAOutputDispatcher.cs
 */

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NIRAAgent.Mind;

public sealed class NIRAOutputDispatcher
    : IDisposable
{
    private readonly Channel<NIRAOutputChunk>
        _channel =
            Channel.CreateUnbounded<NIRAOutputChunk>(
                new UnboundedChannelOptions
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    AllowSynchronousContinuations =
                        false
                });


    private readonly CancellationTokenSource
        _shutdown =
            new();


    private bool
        _disposed;


    public async ValueTask PublishAsync(
        NIRAOutputChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            chunk);


        if (_disposed)
        {
            return;
        }


        await _channel.Writer.WriteAsync(
            chunk,
            cancellationToken);
    }


    public async IAsyncEnumerable<NIRAOutputChunk> ReadAllAsync(
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                _shutdown.Token,
                cancellationToken);


        await foreach (
            NIRAOutputChunk chunk
            in _channel.Reader.ReadAllAsync(
                linked.Token))
        {
            yield return chunk;
        }
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }


        _disposed =
            true;


        _shutdown.Cancel();


        _channel.Writer.TryComplete();


        _shutdown.Dispose();
    }
}

