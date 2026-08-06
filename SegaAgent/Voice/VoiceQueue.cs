/*
 * filename: VoiceQueue.cs
 */

using System.Collections.Concurrent;

namespace SegaAgent.Voice;

public sealed class VoiceQueue : IDisposable
{
    private readonly IVoiceService _voiceService;

    private readonly ConcurrentQueue<string> _queue = new();

    private readonly SemaphoreSlim _signal = new(0);

    private readonly CancellationTokenSource _shutdown =
        new();

    private readonly Task _worker;

    private bool _disposed;


    // =========================================================
    // STATE
    // =========================================================

    public bool IsSpeaking { get; private set; }

    public event EventHandler<bool>? SpeakingChanged;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public VoiceQueue(
        IVoiceService voiceService)
    {
        _voiceService = voiceService;

        _worker =
            Task.Run(
                ProcessQueueAsync
            );
    }


    // =========================================================
    // ENQUEUE
    // =========================================================

    public void Enqueue(
        string text)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _queue.Enqueue(text);

        _signal.Release();
    }


    // =========================================================
    // WORKER
    // =========================================================

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await _signal.WaitAsync(
                    _shutdown.Token
                );

                while (
                    _queue.TryDequeue(
                        out var text))
                {
                    if (_shutdown.IsCancellationRequested)
                    {
                        return;
                    }

                    IsSpeaking = true;

                    SpeakingChanged?.Invoke(
                        this,
                        true
                    );

                    try
                    {
                        await _voiceService.SpeakAsync(
                            text,
                            _shutdown.Token
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // Voice errors must not kill
                        // the entire SegaAI agent.
                    }
                    finally
                    {
                        IsSpeaking = false;

                        SpeakingChanged?.Invoke(
                            this,
                            false
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }


    // =========================================================
    // CLEAR
    // =========================================================

    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
        }
    }


    // =========================================================
    // STOP
    // =========================================================

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _shutdown.Cancel();

        _signal.Release();

        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }


    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _shutdown.Cancel();

        try
        {
            _signal.Release();
        }
        catch
        {
            // Ignore shutdown race.
        }

        _shutdown.Dispose();

        _signal.Dispose();
    }
}