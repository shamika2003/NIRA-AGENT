/*
 * filename: SegaOutputDispatcher.cs
 */

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SegaAgent.Mind;

public sealed class SegaOutputDispatcher
    : IDisposable
{
    private readonly Channel<SegaOutputChunk>
        _channel =
            Channel.CreateUnbounded<SegaOutputChunk>(
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
        SegaOutputChunk chunk,
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


    public async IAsyncEnumerable<SegaOutputChunk> ReadAllAsync(
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                _shutdown.Token,
                cancellationToken);


        await foreach (
            SegaOutputChunk chunk
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
