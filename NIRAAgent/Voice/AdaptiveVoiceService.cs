using System.Diagnostics;
using NIRAAgent.Settings;
namespace NIRAAgent.Voice;

/// <summary>
/// Voice does not depend on the selected reasoning model. Local Piper is always
/// available under the existing voice settings and cancellation policy.
/// </summary>
public sealed class AdaptiveVoiceService : IVoiceService
{
    private readonly PiperVoiceService _piper;
    private readonly NIRARuntimeSettingsService _settings;
    public AdaptiveVoiceService(PiperVoiceService piper,NIRARuntimeSettingsService settings)
    {
        _piper=piper??throw new ArgumentNullException(nameof(piper));
        _settings=settings??throw new ArgumentNullException(nameof(settings));
    }
    public async Task<PreparedVoiceAudio> PrepareAsync(VoiceUtterance utterance,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        cancellationToken.ThrowIfCancellationRequested();
        Debug.WriteLine($"[Voice] Engine=LocalPiper Mode={_settings.Current.VoiceEngine}");
        return await _piper.PrepareAsync(utterance,cancellationToken);
    }
}
