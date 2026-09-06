/*
 * filename: VoiceAudioPlayer.cs
 */

using NAudio.Wave;

using SegaAgent.Settings;

namespace SegaAgent.Voice;

public sealed class VoiceAudioPlayer
{
    private readonly SegaRuntimeSettingsService _settings;

    public VoiceAudioPlayer(
        SegaRuntimeSettingsService settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));
    }

    public async Task PlayAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException
            .ThrowIfNullOrWhiteSpace(
                audioPath);

        if (!File.Exists(
                audioPath))
        {
            throw new FileNotFoundException(
                "Voice audio file was not found.",
                audioPath);
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        using AudioFileReader audioFile =
            new(
                audioPath);

        using WaveOutEvent outputDevice =
            new();

        outputDevice.Volume =
            (float)Math.Clamp(
                _settings.Current.VoiceVolume,
                0.0,
                1.0);

        TaskCompletionSource<bool>
            completion =
                new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

        outputDevice.PlaybackStopped +=
            (_, args) =>
            {
                if (args.Exception !=
                    null)
                {
                    completion
                        .TrySetException(
                            args.Exception);

                    return;
                }

                completion
                    .TrySetResult(
                        true);
            };

        using CancellationTokenRegistration
            registration =
                cancellationToken.Register(
                    () =>
                    {
                        completion
                            .TrySetCanceled(
                                cancellationToken);

                        try
                        {
                            outputDevice.Stop();
                        }
                        catch
                        {
                        }
                    });

        outputDevice.Init(
            audioFile);

        outputDevice.Play();

        await completion.Task;
    }
}
