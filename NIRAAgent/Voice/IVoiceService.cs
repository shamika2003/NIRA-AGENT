/*
 * filename: IVoiceService.cs
 */

namespace NIRAAgent.Voice;

public interface IVoiceService
{
    // =========================================================
    // PREPARE
    //
    // Voice engines synthesize audio here.
    //
    // They DO NOT play audio.
    //
    // Playback belongs to VoiceQueue / VoiceAudioPlayer.
    //
    // This separation allows NIRA to synthesize the next
    // utterance while the current one is already playing.
    // =========================================================

    Task<PreparedVoiceAudio> PrepareAsync(
        VoiceUtterance utterance,
        CancellationToken cancellationToken = default);
}
