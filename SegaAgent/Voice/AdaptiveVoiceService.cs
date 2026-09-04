/*
 * filename: AdaptiveVoiceService.cs
 */

using System.Diagnostics;

using SegaAgent.Voice.Groq;

namespace SegaAgent.Voice;

public sealed class AdaptiveVoiceService
    : IVoiceService
{
    // =========================================================
    // PROVIDERS
    // =========================================================

    private readonly GroqOrpheusVoiceService
        _groq;


    private readonly PiperVoiceService
        _piper;


    // =========================================================
    // MODE
    // =========================================================

    private readonly string
        _mode;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AdaptiveVoiceService(
        GroqOrpheusVoiceService groq,
        PiperVoiceService piper)
    {
        _groq =
            groq
            ?? throw new ArgumentNullException(
                nameof(groq));


        _piper =
            piper
            ?? throw new ArgumentNullException(
                nameof(piper));


        string configuredMode =
            ReadEnvironment(
                "SEGA_VOICE_ENGINE");


        _mode =
            string.IsNullOrWhiteSpace(
                configuredMode)
                ? "auto"
                : configuredMode
                    .ToLowerInvariant();


        if (
            _mode !=
                "auto"
            &&
            _mode !=
                "groq"
            &&
            _mode !=
                "piper")
        {
            throw new InvalidOperationException(
                "SEGA_VOICE_ENGINE must be one of: " +
                "auto, groq, piper.");
        }


        Debug.WriteLine(
            $"[VoiceConfig] " +
            $"Mode='{_mode}' | " +
            $"GroqConfigured={_groq.IsConfigured} | " +
            $"GroqVoice='{_groq.Voice}' | " +
            $"GroqState='{_groq.AvailabilityDescription}'");
    }


    // =========================================================
    // PREPARE
    // =========================================================

    public async Task<PreparedVoiceAudio>
        PrepareAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            utterance);


        cancellationToken
            .ThrowIfCancellationRequested();


        return _mode switch
        {
            "groq" =>
                await PrepareGroqStrictAsync(
                    utterance,
                    cancellationToken),

            "piper" =>
                await PreparePiperAsync(
                    utterance,
                    cancellationToken),

            _ =>
                await PrepareAutoAsync(
                    utterance,
                    cancellationToken)
        };
    }


    // =========================================================
    // AUTO
    // =========================================================

    private async Task<PreparedVoiceAudio>
        PrepareAutoAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken)
    {
        if (
            _groq.IsConfigured
            &&
            _groq.CanAttempt)
        {
            try
            {
                Debug.WriteLine(
                    "[Voice] Synthesis=Groq Orpheus");


                return await _groq.PrepareAsync(
                    utterance,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Voice] Groq synthesis failed. " +
                    $"Falling back to Piper. " +
                    $"Type={ex.GetType().Name} | " +
                    $"Message={ex.Message}");
            }
        }


        return await PreparePiperAsync(
            utterance,
            cancellationToken);
    }


    // =========================================================
    // GROQ STRICT
    // =========================================================

    private async Task<PreparedVoiceAudio>
        PrepareGroqStrictAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken)
    {
        if (!_groq.IsConfigured)
        {
            throw new InvalidOperationException(
                _groq.BuildConfigurationError());
        }


        if (!_groq.CanAttempt)
        {
            throw new InvalidOperationException(
                _groq.AvailabilityDescription);
        }


        Debug.WriteLine(
            "[Voice] Synthesis=Groq Orpheus");


        return await _groq.PrepareAsync(
            utterance,
            cancellationToken);
    }


    // =========================================================
    // PIPER
    // =========================================================

    private async Task<PreparedVoiceAudio>
        PreparePiperAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken)
    {
        Debug.WriteLine(
            "[Voice] Synthesis=Piper fallback");


        return await _piper.PrepareAsync(
            utterance,
            cancellationToken);
    }


    // =========================================================
    // ENVIRONMENT
    // =========================================================

    private static string ReadEnvironment(
        string name)
    {
        string? value =
            Environment
                .GetEnvironmentVariable(
                    name);


        if (!string.IsNullOrWhiteSpace(
                value))
        {
            return value.Trim();
        }


        try
        {
            value =
                Environment
                    .GetEnvironmentVariable(
                        name,
                        EnvironmentVariableTarget.User);


            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }
        catch
        {
        }


        try
        {
            value =
                Environment
                    .GetEnvironmentVariable(
                        name,
                        EnvironmentVariableTarget.Machine);


            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }
        catch
        {
        }


        return string.Empty;
    }
}