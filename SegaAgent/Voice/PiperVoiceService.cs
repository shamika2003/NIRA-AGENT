/*
 * filename: PiperVoiceService.cs
 */

using System.Diagnostics;
using NAudio.Wave;

namespace SegaAgent.Voice;

public sealed class PiperVoiceService : IVoiceService, IDisposable
{
    private readonly string _piperExecutable;
    private readonly string _modelPath;

    private readonly SemaphoreSlim _speechLock =
        new(1, 1);

    private bool _disposed;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PiperVoiceService()
    {
        var baseDirectory =
            AppContext.BaseDirectory;


        var piperDirectory =
            Path.Combine(
                baseDirectory,
                "Piper"
            );


        _piperExecutable =
            Path.Combine(
                piperDirectory,
                "piper.exe"
            );


        _modelPath =
            Path.Combine(
                piperDirectory,
                "Models",
                "en_US-hfc_female-medium.onnx"
            );


        ValidateFiles();
    }


    // =========================================================
    // SPEAK
    // =========================================================

    public async Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }


        ThrowIfDisposed();


        await _speechLock.WaitAsync(
            cancellationToken
        );


        try
        {
            ThrowIfDisposed();


            cancellationToken.ThrowIfCancellationRequested();


            var wavPath =
                Path.Combine(
                    Path.GetTempPath(),
                    $"segaai_tts_{Guid.NewGuid():N}.wav"
                );


            try
            {
                await GenerateSpeechAsync(
                    text,
                    wavPath,
                    cancellationToken
                );


                await PlayAudioAsync(
                    wavPath,
                    cancellationToken
                );
            }
            finally
            {
                DeleteTemporaryFile(
                    wavPath
                );
            }
        }
        finally
        {
            _speechLock.Release();
        }
    }


    // =========================================================
    // GENERATE SPEECH
    // =========================================================

    private async Task GenerateSpeechAsync(
        string text,
        string outputPath,
        CancellationToken cancellationToken)
    {

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    _piperExecutable,

                Arguments =
                    $"--model \"{_modelPath}\" " +
                    $"--output_file \"{outputPath}\"",

                WorkingDirectory =
                    Path.GetDirectoryName(
                        _piperExecutable
                    )!,

                UseShellExecute =
                    false,

                RedirectStandardInput =
                    true,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                CreateNoWindow =
                    true
            };


        using var process =
            new Process
            {
                StartInfo = startInfo
            };


        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start Piper."
            );
        }


        try
        {
            await process.StandardInput.WriteAsync(
                text.AsMemory(),
                cancellationToken
            );


            await process.StandardInput.FlushAsync(
                cancellationToken
            );


            process.StandardInput.Close();


            var errorTask =
                process.StandardError.ReadToEndAsync(
                    cancellationToken
                );


            var outputTask =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken
                );


            await process.WaitForExitAsync(
                cancellationToken
            );


            var error =
                await errorTask;


            _ = await outputTask;


            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Piper failed with exit code " +
                    $"{process.ExitCode}. " +
                    $"Error: {error}"
                );
            }


            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    "Piper completed but did not create " +
                    "the expected WAV file."
                );
            }
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(
                process
            );

            throw;
        }
        catch
        {
            TryKillProcess(
                process
            );

            throw;
        }
    }


    // =========================================================
    // PLAY AUDIO
    // =========================================================

    private static async Task PlayAudioAsync(
        string wavPath,
        CancellationToken cancellationToken)
    {
        using var audioFile =
            new AudioFileReader(
                wavPath
            );


        using var outputDevice =
            new WaveOutEvent();


        outputDevice.Init(
            audioFile
        );


        var completion =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );


        void OnPlaybackStopped(
            object? sender,
            StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                completion.TrySetException(
                    e.Exception
                );

                return;
            }


            completion.TrySetResult(
                true
            );
        }


        outputDevice.PlaybackStopped +=
            OnPlaybackStopped;


        using var registration =
            cancellationToken.Register(
                () =>
                {
                    try
                    {
                        outputDevice.Stop();
                    }
                    catch
                    {
                        // Ignore playback shutdown race.
                    }
                }
            );


        try
        {
            outputDevice.Play();


            await completion.Task;


            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            outputDevice.PlaybackStopped -=
                OnPlaybackStopped;
        }
    }


    // =========================================================
    // VALIDATE FILES
    // =========================================================

    private void ValidateFiles()
    {
        if (!File.Exists(
                _piperExecutable))
        {
            throw new FileNotFoundException(
                "Piper executable was not found.",
                _piperExecutable
            );
        }


        if (!File.Exists(
                _modelPath))
        {
            throw new FileNotFoundException(
                "Piper voice model was not found.",
                _modelPath
            );
        }


        var configPath =
            _modelPath + ".json";


        if (!File.Exists(
                configPath))
        {
            throw new FileNotFoundException(
                "Piper voice configuration was not found.",
                configPath
            );
        }
    }


    // =========================================================
    // KILL PROCESS
    // =========================================================

    private static void TryKillProcess(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true
                );
            }
        }
        catch
        {
            // Ignore process shutdown race.
        }
    }


    // =========================================================
    // DELETE TEMP FILE
    // =========================================================

    private static void DeleteTemporaryFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary-file cleanup failure should
            // not crash the voice pipeline.
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


        _speechLock.Dispose();
    }


    // =========================================================
    // DISPOSE GUARD
    // =========================================================

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(PiperVoiceService)
            );
        }
    }
}