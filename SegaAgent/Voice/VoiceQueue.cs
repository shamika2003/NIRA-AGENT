/*
 * filename: VoiceQueue.cs
 */

using System.Diagnostics;
using System.Threading.Channels;

using SegaAgent.Agent.State;

namespace SegaAgent.Voice;

public sealed class VoiceQueue
    : IDisposable
{
    // =========================================================
    // SERVICES
    // =========================================================

    private readonly IVoiceService
        _voiceService;


    private readonly VoiceAudioPlayer
        _audioPlayer;


    private readonly SegaStateService
        _state;


    // =========================================================
    // UTTERANCE CHANNEL
    //
    // Receives speech units from the streaming responder.
    // =========================================================

    private readonly Channel<
        QueuedVoiceUtterance>
        _utterances;


    // =========================================================
    // PREPARED AUDIO CHANNEL
    //
    // Only one completed future utterance is allowed to wait
    // here.
    // =========================================================

    private readonly Channel<
        PreparedVoiceItem>
        _prepared;


    // =========================================================
    // PREFETCH PERMIT
    //
    // This is important.
    //
    // A bounded prepared channel alone is NOT sufficient to
    // limit cloud synthesis to one item ahead.
    //
    // Without this permit:
    //
    // sequence 2 may sit prepared in the channel
    // while sequence 3 is already being synthesized.
    //
    // That could waste Groq quota if the user interrupts.
    //
    // This permit means:
    //
    // current audio playing
    //        +
    // maximum ONE future audio being prepared/ready
    // =========================================================

    private readonly SemaphoreSlim
        _prefetchPermit =
            new(
                1,
                1);


    // =========================================================
    // SHUTDOWN
    // =========================================================

    private readonly CancellationTokenSource
        _shutdown =
            new();


    // =========================================================
    // GENERATION
    //
    // Every interruption increments this value.
    //
    // Audio produced for an older generation is never allowed
    // to play afterward.
    // =========================================================

    private long
        _generation;


    // =========================================================
    // ACTIVE WORK CANCELLATION
    // =========================================================

    private readonly object
        _workLock =
            new();


    private CancellationTokenSource?
        _currentSynthesisCancellation;


    private CancellationTokenSource?
        _currentPlaybackCancellation;


    // =========================================================
    // WORKERS
    // =========================================================

    private readonly Task
        _synthesisWorker;


    private readonly Task
        _playbackWorker;


    // =========================================================
    // SPEAKING STATE
    // =========================================================

    private int
        _isSpeaking;


    // =========================================================
    // LIFETIME
    // =========================================================

    private bool
        _disposed;


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public bool IsSpeaking =>
        Volatile.Read(
            ref _isSpeaking) ==
        1;


    public event EventHandler<bool>?
        SpeakingChanged;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public VoiceQueue(
        IVoiceService voiceService,
        VoiceAudioPlayer audioPlayer,
        SegaStateService state)
    {
        _voiceService =
            voiceService
            ?? throw new ArgumentNullException(
                nameof(voiceService));


        _audioPlayer =
            audioPlayer
            ?? throw new ArgumentNullException(
                nameof(audioPlayer));


        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        // =====================================================
        // INPUT
        //
        // Multiple writers:
        //
        // user response
        // background response
        //
        // Multiple readers are allowed because Interrupt/Clear
        // may drain the channel while the worker exists.
        // =====================================================

        _utterances =
            Channel.CreateUnbounded<
                QueuedVoiceUtterance>(
                    new UnboundedChannelOptions
                    {
                        SingleReader =
                            false,

                        SingleWriter =
                            false,

                        AllowSynchronousContinuations =
                            false
                    });


        // =====================================================
        // PREPARED AUDIO
        // =====================================================

        _prepared =
            Channel.CreateBounded<
                PreparedVoiceItem>(
                    new BoundedChannelOptions(
                        1)
                    {
                        SingleReader =
                            false,

                        SingleWriter =
                            true,

                        FullMode =
                            BoundedChannelFullMode.Wait,

                        AllowSynchronousContinuations =
                            false
                    });


        // =====================================================
        // START PIPELINE
        // =====================================================

        _synthesisWorker =
            Task.Run(
                SynthesisLoopAsync);


        _playbackWorker =
            Task.Run(
                PlaybackLoopAsync);
    }


    // =========================================================
    // ENQUEUE
    // =========================================================

    public void Enqueue(
        VoiceUtterance utterance)
    {
        if (_disposed)
        {
            return;
        }


        ArgumentNullException.ThrowIfNull(
            utterance);


        if (!SpeechChunker
            .ContainsSpeakableContent(
                utterance.Text))
        {
            return;
        }


        long generation =
            Volatile.Read(
                ref _generation);


        QueuedVoiceUtterance queued =
            new(
                generation,
                utterance);


        if (!_utterances
            .Writer
            .TryWrite(
                queued))
        {
            Debug.WriteLine(
                "[VoiceQueue] " +
                "Utterance rejected because the " +
                "voice pipeline is stopping.");
        }
    }


    // =========================================================
    // SYNTHESIS LOOP
    // =========================================================

    private async Task SynthesisLoopAsync()
    {
        try
        {
            await foreach (
                QueuedVoiceUtterance queued
                in _utterances
                    .Reader
                    .ReadAllAsync(
                        _shutdown.Token))
            {
                if (IsStale(
                        queued.Generation))
                {
                    continue;
                }


                bool permitHeld =
                    false;


                PreparedVoiceAudio?
                    preparedAudio =
                        null;


                try
                {
                    // =========================================
                    // ONE-AHEAD LIMIT
                    // =========================================

                    await _prefetchPermit
                        .WaitAsync(
                            _shutdown.Token);


                    permitHeld =
                        true;


                    if (IsStale(
                            queued.Generation))
                    {
                        continue;
                    }


                    using CancellationTokenSource
                        synthesisCancellation =
                            CancellationTokenSource
                                .CreateLinkedTokenSource(
                                    _shutdown.Token);


                    SetCurrentSynthesisCancellation(
                        synthesisCancellation);


                    try
                    {
                        Debug.WriteLine(
                            $"[VoicePrefetch] START | " +
                            $"Generation=" +
                            $"{queued.Generation} | " +
                            $"Response=" +
                            $"{queued.Utterance.ResponseId} | " +
                            $"Sequence=" +
                            $"{queued.Utterance.Sequence}");


                        preparedAudio =
                            await _voiceService
                                .PrepareAsync(
                                    queued.Utterance,
                                    synthesisCancellation.Token);


                        synthesisCancellation
                            .Token
                            .ThrowIfCancellationRequested();


                        // =====================================
                        // INTERRUPTION RACE CHECK
                        // =====================================

                        if (IsStale(
                                queued.Generation))
                        {
                            preparedAudio.Dispose();


                            preparedAudio =
                                null;


                            continue;
                        }


                        PreparedVoiceItem item =
                            new(
                                queued.Generation,
                                preparedAudio);


                        await _prepared
                            .Writer
                            .WriteAsync(
                                item,
                                synthesisCancellation.Token);


                        Debug.WriteLine(
                            $"[VoicePrefetch] READY | " +
                            $"Generation=" +
                            $"{queued.Generation} | " +
                            $"Response=" +
                            $"{queued.Utterance.ResponseId} | " +
                            $"Sequence=" +
                            $"{queued.Utterance.Sequence} | " +
                            $"Engine='" +
                            $"{preparedAudio.Engine}'");


                        // =====================================
                        // OWNERSHIP TRANSFER
                        //
                        // Prepared channel now owns:
                        //
                        // - audio
                        // - prefetch permit
                        //
                        // Playback/drain will release them.
                        // =====================================

                        preparedAudio =
                            null;


                        permitHeld =
                            false;
                    }
                    finally
                    {
                        ClearCurrentSynthesisCancellation(
                            synthesisCancellation);
                    }
                }
                catch (OperationCanceledException)
                    when (!_shutdown
                        .IsCancellationRequested)
                {
                    /*
                     * Normal Sega speech interruption.
                     */
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[VoiceQueue] " +
                        $"SYNTHESIS ERROR: {ex}");
                }
                finally
                {
                    preparedAudio?
                        .Dispose();


                    if (permitHeld)
                    {
                        ReleasePrefetchPermit();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            /*
             * Normal application shutdown.
             */
        }
        finally
        {
            _prepared
                .Writer
                .TryComplete();
        }
    }


    // =========================================================
    // PLAYBACK LOOP
    // =========================================================

    private async Task PlaybackLoopAsync()
    {
        try
        {
            while (
                await _prepared
                    .Reader
                    .WaitToReadAsync(
                        _shutdown.Token))
            {
                SetSpeaking(
                    true);


                try
                {
                    while (
                        _prepared
                            .Reader
                            .TryRead(
                                out PreparedVoiceItem
                                    item))
                    {
                        // =====================================
                        // ITEM LEFT THE PREFETCH SLOT.
                        //
                        // The synthesis worker may now prepare
                        // exactly one next utterance while this
                        // one is playing.
                        // =====================================

                        ReleasePrefetchPermit();


                        if (IsStale(
                                item.Generation))
                        {
                            item.Audio.Dispose();


                            continue;
                        }


                        await PlayPreparedAsync(
                            item);
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
            /*
             * Normal shutdown.
             */
        }
        finally
        {
            SetSpeaking(
                false);


            DrainPreparedAudio();
        }
    }


    // =========================================================
    // PLAY PREPARED AUDIO
    // =========================================================

    private async Task PlayPreparedAsync(
        PreparedVoiceItem item)
    {
        using CancellationTokenSource
            playbackCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        _shutdown.Token);


        SetCurrentPlaybackCancellation(
            playbackCancellation);


        try
        {
            Debug.WriteLine(
                $"[VoicePlayback] START | " +
                $"Generation={item.Generation} | " +
                $"Response=" +
                $"{item.Audio.Utterance.ResponseId} | " +
                $"Sequence=" +
                $"{item.Audio.Utterance.Sequence} | " +
                $"Engine='{item.Audio.Engine}'");


            foreach (
                string audioPath
                in item.Audio.AudioPaths)
            {
                playbackCancellation
                    .Token
                    .ThrowIfCancellationRequested();


                if (IsStale(
                        item.Generation))
                {
                    return;
                }


                await _audioPlayer
                    .PlayAsync(
                        audioPath,
                        playbackCancellation.Token);
            }


            Debug.WriteLine(
                $"[VoicePlayback] END | " +
                $"Response=" +
                $"{item.Audio.Utterance.ResponseId} | " +
                $"Sequence=" +
                $"{item.Audio.Utterance.Sequence}");
        }
        catch (OperationCanceledException)
            when (!_shutdown
                .IsCancellationRequested)
        {
            /*
             * User interrupted Sega.
             */
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VoiceQueue] " +
                $"PLAYBACK ERROR: {ex}");
        }
        finally
        {
            ClearCurrentPlaybackCancellation(
                playbackCancellation);


            item.Audio.Dispose();
        }
    }


    // =========================================================
    // INTERRUPT
    //
    // Used when the user starts another interaction.
    //
    // This immediately invalidates:
    //
    // - currently playing speech
    // - currently synthesizing speech
    // - queued speech
    // - prefetched audio
    // =========================================================

    public void Interrupt()
    {
        if (_disposed)
        {
            return;
        }


        long generation =
            Interlocked.Increment(
                ref _generation);


        Debug.WriteLine(
            $"[VoiceQueue] INTERRUPT | " +
            $"Generation={generation}");


        CancelCurrentWork();


        DrainUtterances();


        DrainPreparedAudio();


        SetSpeaking(
            false);
    }


    // =========================================================
    // CLEAR FUTURE SPEECH
    //
    // Does not intentionally cancel the audio currently being
    // played.
    // =========================================================

    public void Clear()
    {
        if (_disposed)
        {
            return;
        }


        DrainUtterances();


        DrainPreparedAudio();
    }


    // =========================================================
    // GENERATION CHECK
    // =========================================================

    private bool IsStale(
        long generation)
    {
        return generation !=
            Volatile.Read(
                ref _generation);
    }


    // =========================================================
    // SYNTHESIS CANCELLATION
    // =========================================================

    private void SetCurrentSynthesisCancellation(
        CancellationTokenSource source)
    {
        lock (_workLock)
        {
            _currentSynthesisCancellation =
                source;
        }
    }


    private void ClearCurrentSynthesisCancellation(
        CancellationTokenSource source)
    {
        lock (_workLock)
        {
            if (ReferenceEquals(
                    _currentSynthesisCancellation,
                    source))
            {
                _currentSynthesisCancellation =
                    null;
            }
        }
    }


    // =========================================================
    // PLAYBACK CANCELLATION
    // =========================================================

    private void SetCurrentPlaybackCancellation(
        CancellationTokenSource source)
    {
        lock (_workLock)
        {
            _currentPlaybackCancellation =
                source;
        }
    }


    private void ClearCurrentPlaybackCancellation(
        CancellationTokenSource source)
    {
        lock (_workLock)
        {
            if (ReferenceEquals(
                    _currentPlaybackCancellation,
                    source))
            {
                _currentPlaybackCancellation =
                    null;
            }
        }
    }


    // =========================================================
    // CANCEL CURRENT WORK
    // =========================================================

    private void CancelCurrentWork()
    {
        lock (_workLock)
        {
            try
            {
                _currentSynthesisCancellation?
                    .Cancel();
            }
            catch
            {
            }


            try
            {
                _currentPlaybackCancellation?
                    .Cancel();
            }
            catch
            {
            }
        }
    }


    // =========================================================
    // DRAIN UTTERANCES
    // =========================================================

    private void DrainUtterances()
    {
        while (
            _utterances
                .Reader
                .TryRead(
                    out _))
        {
        }
    }


    // =========================================================
    // DRAIN PREPARED AUDIO
    // =========================================================

    private void DrainPreparedAudio()
    {
        while (
            _prepared
                .Reader
                .TryRead(
                    out PreparedVoiceItem
                        item))
        {
            /*
             * The item owned one prefetch permit while it was
             * waiting inside the prepared channel.
             */

            ReleasePrefetchPermit();


            item.Audio.Dispose();
        }
    }


    // =========================================================
    // PREFETCH PERMIT RELEASE
    // =========================================================

    private void ReleasePrefetchPermit()
    {
        try
        {
            _prefetchPermit.Release();
        }
        catch (SemaphoreFullException)
        {
            /*
             * Protect shutdown/interruption races.
             *
             * A duplicate release should never break the
             * application.
             */
        }
    }


    // =========================================================
    // SPEAKING STATE
    // =========================================================

    private void SetSpeaking(
        bool speaking)
    {
        int desired =
            speaking
                ? 1
                : 0;


        int previous =
            Interlocked.Exchange(
                ref _isSpeaking,
                desired);


        if (previous ==
            desired)
        {
            return;
        }


        _state.SetSpeaking(
            speaking);


        SpeakingChanged?.Invoke(
            this,
            speaking);
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


        Interlocked.Increment(
            ref _generation);


        CancelCurrentWork();


        DrainUtterances();


        DrainPreparedAudio();


        _utterances
            .Writer
            .TryComplete();


        _shutdown.Cancel();


        try
        {
            await Task.WhenAll(
                _synthesisWorker,
                _playbackWorker);
        }
        catch (OperationCanceledException)
        {
        }


        SetSpeaking(
            false);
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


        Interlocked.Increment(
            ref _generation);


        CancelCurrentWork();


        DrainUtterances();


        DrainPreparedAudio();


        _utterances
            .Writer
            .TryComplete();


        _shutdown.Cancel();


        try
        {
            Task.WhenAll(
                    _synthesisWorker,
                    _playbackWorker)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }


        DrainPreparedAudio();


        SetSpeaking(
            false);


        _prefetchPermit.Dispose();


        _shutdown.Dispose();
    }


    // =========================================================
    // QUEUED UTTERANCE
    // =========================================================

    private sealed record
        QueuedVoiceUtterance(
            long Generation,
            VoiceUtterance Utterance);


    // =========================================================
    // PREPARED AUDIO ITEM
    // =========================================================

    private sealed record
        PreparedVoiceItem(
            long Generation,
            PreparedVoiceAudio Audio);
}