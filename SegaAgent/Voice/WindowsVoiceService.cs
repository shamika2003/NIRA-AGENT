/*
 * filename: WindowsVoiceService.cs
 */

using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace SegaAgent.Voice;

[SupportedOSPlatform("windows")]
public sealed class WindowsVoiceService : IVoiceService, IDisposable
{
    private readonly SpeechSynthesizer _speech;

    private readonly object _sync = new();

    private TaskCompletionSource<bool>? _currentCompletion;

    private bool _disposed;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public WindowsVoiceService()
    {
        _speech = new SpeechSynthesizer();

        _speech.Rate = 0;
        _speech.Volume = 100;

        SelectVoice();
    }


    // =========================================================
    // SELECT VOICE
    // =========================================================

    private void SelectVoice()
    {
        var voices =
            _speech.GetInstalledVoices();

        if (voices.Count == 0)
        {
            throw new InvalidOperationException(
                "No Windows speech voices are installed."
            );
        }


        // -----------------------------------------------------
        // DEBUG:
        // Print all installed voices.
        // -----------------------------------------------------

        foreach (var voice in voices)
        {
            var info = voice.VoiceInfo;

            System.Diagnostics.Debug.WriteLine(
                $"VOICE: {info.Name} | " +
                $"CULTURE: {info.Culture.Name} | " +
                $"GENDER: {info.Gender}"
            );
        }


        // -----------------------------------------------------
        // 1. Prefer Microsoft Zira
        // -----------------------------------------------------

        var zira =
            voices
                .Select(v => v.VoiceInfo)
                .FirstOrDefault(v =>
                    v.Name.Contains(
                        "Zira",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

        if (zira != null)
        {
            _speech.SelectVoice(zira.Name);
            return;
        }


        // -----------------------------------------------------
        // 2. Prefer an English female voice
        // -----------------------------------------------------

        var femaleEnglish =
            voices
                .Select(v => v.VoiceInfo)
                .FirstOrDefault(v =>
                    v.Gender == VoiceGender.Female &&
                    v.Culture.Name.StartsWith(
                        "en",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

        if (femaleEnglish != null)
        {
            _speech.SelectVoice(femaleEnglish.Name);
            return;
        }


        // -----------------------------------------------------
        // 3. Any female voice
        // -----------------------------------------------------

        var female =
            voices
                .Select(v => v.VoiceInfo)
                .FirstOrDefault(v =>
                    v.Gender == VoiceGender.Female
                );

        if (female != null)
        {
            _speech.SelectVoice(female.Name);
            return;
        }


        // -----------------------------------------------------
        // 4. Fall back to Windows default voice
        // -----------------------------------------------------

        _speech.SelectVoice(
            _speech.Voice.Name
        );
    }


    // =========================================================
    // SPEAK
    // =========================================================

    public Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------------------------
            // A single WindowsVoiceService instance should only
            // have one active speech operation at a time.
            //
            // VoiceQueue normally guarantees this, but this
            // guard protects the service itself as well.
            // -------------------------------------------------

            if (_currentCompletion != null)
            {
                throw new InvalidOperationException(
                    "Speech is already in progress."
                );
            }


            var completion =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

            _currentCompletion = completion;


            void OnCompleted(
                object? sender,
                SpeakCompletedEventArgs e)
            {
                _speech.SpeakCompleted -= OnCompleted;

                lock (_sync)
                {
                    if (ReferenceEquals(
                            _currentCompletion,
                            completion))
                    {
                        _currentCompletion = null;
                    }
                }


                if (e.Cancelled)
                {
                    completion.TrySetCanceled(
                        cancellationToken
                    );

                    return;
                }


                if (e.Error != null)
                {
                    completion.TrySetException(
                        e.Error
                    );

                    return;
                }


                completion.TrySetResult(true);
            }


            _speech.SpeakCompleted += OnCompleted;


            // -------------------------------------------------
            // Cancellation registration
            //
            // If VoiceQueue cancels the token while Windows is
            // speaking, actually stop SpeechSynthesizer.
            // -------------------------------------------------

            var registration =
                cancellationToken.Register(
                    static state =>
                    {
                        var speech =
                            (SpeechSynthesizer)state!;

                        try
                        {
                            speech.SpeakAsyncCancelAll();
                        }
                        catch
                        {
                            // Ignore cancellation race.
                        }
                    },
                    _speech
                );


            // Dispose the registration after the speech
            // operation finishes.
            _ = completion.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );


            try
            {
                _speech.SpeakAsync(text);
            }
            catch
            {
                _speech.SpeakCompleted -= OnCompleted;

                registration.Dispose();

                _currentCompletion = null;

                throw;
            }


            return completion.Task;
        }
    }


    // =========================================================
    // STOP
    // =========================================================

    public void Stop()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _speech.SpeakAsyncCancelAll();
            }
            catch
            {
                // Ignore shutdown/cancellation race.
            }
        }
    }


    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _speech.SpeakAsyncCancelAll();
            }
            catch
            {
                // Ignore shutdown race.
            }

            _speech.Dispose();

            _currentCompletion = null;
        }
    }


    // =========================================================
    // DISPOSE GUARD
    // =========================================================

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(WindowsVoiceService)
            );
        }
    }
}
