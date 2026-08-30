/*
 * filename: VoiceQueue.cs
 */

using System.Collections.Concurrent;

using SegaAgent.Agent.State;

namespace SegaAgent.Voice;

public sealed class VoiceQueue : IDisposable
{
    private readonly IVoiceService _voiceService;

    private readonly SegaStateService _state;


    private readonly ConcurrentQueue<string>
        _queue =
            new();


    private readonly SemaphoreSlim _signal =
        new(0);


    private readonly CancellationTokenSource
        _shutdown =
            new();


    private readonly object _speechLock =
        new();


    private CancellationTokenSource?
        _currentSpeechCancellation;


    private readonly Task _worker;


    private bool _disposed;


    // =========================================================
    // STATE
    // =========================================================

    public bool IsSpeaking
    {
        get;
        private set;
    }


    public event EventHandler<bool>?
        SpeakingChanged;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public VoiceQueue(
        IVoiceService voiceService,
        SegaStateService state)
    {
        _voiceService =
            voiceService;


        _state =
            state;


        _worker =
            Task.Run(
                ProcessQueueAsync);
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


        if (string.IsNullOrWhiteSpace(
                text))
        {
            return;
        }


        _queue.Enqueue(
            text);


        _signal.Release();
    }


    // =========================================================
    // WORKER
    // =========================================================

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (!_shutdown
                .IsCancellationRequested)
            {
                await _signal.WaitAsync(
                    _shutdown.Token);


                if (!_queue.TryDequeue(
                        out var text))
                {
                    continue;
                }


                SetSpeaking(
                    true);


                try
                {
                    while (true)
                    {
                        if (_shutdown
                            .IsCancellationRequested)
                        {
                            return;
                        }


                        using var speechCancellation =
                            CancellationTokenSource
                                .CreateLinkedTokenSource(
                                    _shutdown.Token);


                        SetCurrentSpeechCancellation(
                            speechCancellation);


                        try
                        {
                            await _voiceService
                                .SpeakAsync(
                                    text,
                                    speechCancellation.Token);
                        }
                        catch (OperationCanceledException)
                            when (!_shutdown
                                .IsCancellationRequested)
                        {
                            /*
                             * Current speech was intentionally
                             * interrupted.
                             */
                        }
                        finally
                        {
                            ClearCurrentSpeechCancellation(
                                speechCancellation);
                        }


                        if (!_queue.TryDequeue(
                                out text))
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    SetSpeaking(
                        false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        finally
        {
            SetSpeaking(
                false);
        }
    }


    // =========================================================
    // SET SPEAKING
    // =========================================================

    private void SetSpeaking(
        bool speaking)
    {
        if (IsSpeaking ==
            speaking)
        {
            return;
        }


        IsSpeaking =
            speaking;


        _state.SetSpeaking(
            speaking);


        SpeakingChanged?.Invoke(
            this,
            speaking);
    }


    // =========================================================
    // CURRENT SPEECH
    // =========================================================

    private void SetCurrentSpeechCancellation(
        CancellationTokenSource source)
    {
        lock (_speechLock)
        {
            _currentSpeechCancellation =
                source;
        }
    }


    private void ClearCurrentSpeechCancellation(
        CancellationTokenSource source)
    {
        lock (_speechLock)
        {
            if (ReferenceEquals(
                    _currentSpeechCancellation,
                    source))
            {
                _currentSpeechCancellation =
                    null;
            }
        }
    }


    // =========================================================
    // INTERRUPT
    //
    // Used when the user starts a new interaction.
    // =========================================================

    public void Interrupt()
    {
        Clear();


        lock (_speechLock)
        {
            try
            {
                _currentSpeechCancellation?
                    .Cancel();
            }
            catch
            {
            }
        }
    }


    // =========================================================
    // CLEAR
    // =========================================================

    public void Clear()
    {
        while (_queue.TryDequeue(
            out _))
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


        lock (_speechLock)
        {
            try
            {
                _currentSpeechCancellation?
                    .Cancel();
            }
            catch
            {
            }
        }


        try
        {
            _signal.Release();
        }
        catch
        {
        }


        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
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


        _disposed =
            true;


        _shutdown.Cancel();


        lock (_speechLock)
        {
            try
            {
                _currentSpeechCancellation?
                    .Cancel();
            }
            catch
            {
            }
        }


        try
        {
            _signal.Release();
        }
        catch
        {
        }


        _state.SetSpeaking(
            false);


        _shutdown.Dispose();

        _signal.Dispose();
    }
}