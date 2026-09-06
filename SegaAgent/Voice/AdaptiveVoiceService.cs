/*
 * filename: AdaptiveVoiceService.cs
 */

using System.Diagnostics;

using SegaAgent.Settings;
using SegaAgent.Voice.Groq;

namespace SegaAgent.Voice;

public sealed class AdaptiveVoiceService
    : IVoiceService
{
    private readonly GroqOrpheusVoiceService _groq;
    private readonly PiperVoiceService _piper;
    private readonly SegaRuntimeSettingsService _settings;

    public AdaptiveVoiceService(
        GroqOrpheusVoiceService groq,
        PiperVoiceService piper,
        SegaRuntimeSettingsService settings)
    {
        _groq = groq ?? throw new ArgumentNullException(nameof(groq));
        _piper = piper ?? throw new ArgumentNullException(nameof(piper));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Debug.WriteLine(
            $"[VoiceConfig] Mode='{_settings.Current.VoiceEngine}' | " +
            $"GroqConfigured={_groq.IsConfigured} | " +
            $"GroqVoice='{_groq.Voice}' | " +
            $"GroqState='{_groq.AvailabilityDescription}'");
    }

    public async Task<PreparedVoiceAudio> PrepareAsync(
        VoiceUtterance utterance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        cancellationToken.ThrowIfCancellationRequested();

        return _settings.Current.VoiceEngine switch
        {
            SegaVoiceEngineMode.Groq =>
                await PrepareGroqStrictAsync(utterance, cancellationToken),

            SegaVoiceEngineMode.Piper =>
                await PreparePiperAsync(utterance, cancellationToken),

            _ =>
                await PrepareAutoAsync(utterance, cancellationToken)
        };
    }

    private async Task<PreparedVoiceAudio> PrepareAutoAsync(
        VoiceUtterance utterance,
        CancellationToken cancellationToken)
    {
        if (_groq.IsConfigured && _groq.CanAttempt)
        {
            try
            {
                Debug.WriteLine("[Voice] Synthesis=Groq Orpheus");
                return await _groq.PrepareAsync(utterance, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Voice] Groq synthesis failed. Falling back to Piper. " +
                    $"Type={ex.GetType().Name} | Message={ex.Message}");
            }
        }

        return await PreparePiperAsync(utterance, cancellationToken);
    }

    private async Task<PreparedVoiceAudio> PrepareGroqStrictAsync(
        VoiceUtterance utterance,
        CancellationToken cancellationToken)
    {
        if (!_groq.IsConfigured)
        {
            throw new InvalidOperationException(_groq.BuildConfigurationError());
        }

        if (!_groq.CanAttempt)
        {
            throw new InvalidOperationException(_groq.AvailabilityDescription);
        }

        Debug.WriteLine("[Voice] Synthesis=Groq Orpheus");
        return await _groq.PrepareAsync(utterance, cancellationToken);
    }

    private async Task<PreparedVoiceAudio> PreparePiperAsync(
        VoiceUtterance utterance,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine("[Voice] Synthesis=Piper");
        return await _piper.PrepareAsync(utterance, cancellationToken);
    }
}
