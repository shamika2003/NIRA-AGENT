/*
 * filename: NIRABackgroundProcessor.cs
 */

using NIRAAgent.Perception;

namespace NIRAAgent.Mind;

public sealed class NIRABackgroundProcessor
{
    private readonly NIRAMindRuntime
        _runtime;


    private readonly NIRAOutputDispatcher
        _dispatcher;


    public NIRABackgroundProcessor(
        NIRAMindRuntime runtime,
        NIRAOutputDispatcher dispatcher)
    {
        _runtime =
            runtime
            ?? throw new ArgumentNullException(
                nameof(runtime));


        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(
                nameof(dispatcher));
    }


    public async Task ProcessInternalAsync(
        NIRAMindEvent mindEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        await PublishAsync(
            _runtime.ProcessInternalAsync(
                mindEvent,
                cancellationToken),
            cancellationToken);
    }


    public async Task ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        await PublishAsync(
            _runtime.ProcessPerceptionAsync(
                perception,
                cancellationToken),
            cancellationToken);
    }


    public async Task ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        await PublishAsync(
            _runtime.ProcessProactiveAsync(
                perception,
                cancellationToken),
            cancellationToken);
    }


    private async Task PublishAsync(
        IAsyncEnumerable<NIRAOutputChunk> output,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                NIRAOutputChunk chunk
                in output.WithCancellation(
                    cancellationToken))
            {
                await _dispatcher.PublishAsync(
                    chunk,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            // A run-local cancellation may be surfaced later as
            // a dedicated executive event. It is not shutdown.
        }
    }
}
