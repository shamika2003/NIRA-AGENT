/*
 * filename: IVoiceService.cs
 */

namespace SegaAgent.Voice;

public interface IVoiceService
{
    Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default);
}