/*
 * filename: PiperVoiceService.cs
 */

using System.Diagnostics;
using System.Globalization;

namespace NIRAAgent.Voice;

public sealed class PiperVoiceService
    : IVoiceService,
      IDisposable
{
    // =========================================================
    // PIPER
    // =========================================================

    private readonly string
        _piperExecutable;


    private readonly string
        _modelPath;


    // =========================================================
    // SYNTHESIS LOCK
    // =========================================================

    private readonly SemaphoreSlim
        _speechLock =
            new(
                1,
                1);


    // =========================================================
    // LIFETIME
    // =========================================================

    private bool
        _disposed;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PiperVoiceService()
    {
        string baseDirectory =
            AppContext.BaseDirectory;


        string piperDirectory =
            Path.Combine(
                baseDirectory,
                "Piper");


        _piperExecutable =
            Path.Combine(
                piperDirectory,
                "piper.exe");


        _modelPath =
            Path.Combine(
                piperDirectory,
                "Models",
                "en_US-hfc_female-medium.onnx");


        ValidateFiles();
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


        if (!SpeechChunker
            .ContainsSpeakableContent(
                utterance.Text))
        {
            throw new ArgumentException(
                "Piper received a non-speakable utterance.",
                nameof(utterance));
        }


        ThrowIfDisposed();


        await _speechLock.WaitAsync(
            cancellationToken);


        string? wavPath =
            null;


        bool ownershipTransferred =
            false;


        try
        {
            ThrowIfDisposed();


            cancellationToken
                .ThrowIfCancellationRequested();


            NIRAVoiceExpression expression =
                utterance
                    .Expression
                    .Normalize();


            // =================================================
            // PIPER EXPRESSION
            // =================================================

            double lengthScale =
                Math.Clamp(
                    1.0 /
                    expression.Pace,
                    0.84,
                    1.18);


            double noiseScale =
                Math.Clamp(
                    0.62
                    +
                    expression.Arousal *
                        0.08
                    +
                    expression.Playfulness *
                        0.04
                    -
                    expression.Restraint *
                        0.05,
                    0.52,
                    0.78);


            double noiseW =
                Math.Clamp(
                    0.72
                    +
                    expression.Playfulness *
                        0.08
                    +
                    expression.Arousal *
                        0.05
                    -
                    expression.Restraint *
                        0.06,
                    0.60,
                    0.90);


            // =================================================
            // OUTPUT
            // =================================================

            wavPath =
                Path.Combine(
                    Path.GetTempPath(),
                    $"NIRAai_piper_" +
                    $"{Guid.NewGuid():N}.wav");


            await GenerateSpeechAsync(
                utterance.Text,
                wavPath,
                lengthScale,
                noiseScale,
                noiseW,
                cancellationToken);


            cancellationToken
                .ThrowIfCancellationRequested();


            Debug.WriteLine(
                $"[Piper] Prepared | " +
                $"Response={utterance.ResponseId} | " +
                $"Sequence={utterance.Sequence}");


            PreparedVoiceAudio prepared =
                new(
                    utterance,
                    new[]
                    {
                        wavPath
                    },
                    "Piper");


            ownershipTransferred =
                true;


            return prepared;
        }
        finally
        {
            if (
                !ownershipTransferred
                &&
                !string.IsNullOrWhiteSpace(
                    wavPath))
            {
                DeleteTemporaryFile(
                    wavPath);
            }


            _speechLock.Release();
        }
    }


    // =========================================================
    // GENERATE
    // =========================================================

    private async Task GenerateSpeechAsync(
        string text,
        string outputPath,
        double lengthScale,
        double noiseScale,
        double noiseW,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    _piperExecutable,

                WorkingDirectory =
                    Path.GetDirectoryName(
                        _piperExecutable)!,

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


        startInfo.ArgumentList.Add(
            "--model");


        startInfo.ArgumentList.Add(
            _modelPath);


        startInfo.ArgumentList.Add(
            "--output_file");


        startInfo.ArgumentList.Add(
            outputPath);


        startInfo.ArgumentList.Add(
            "--length_scale");


        startInfo.ArgumentList.Add(
            lengthScale.ToString(
                "0.###",
                CultureInfo.InvariantCulture));


        startInfo.ArgumentList.Add(
            "--noise_scale");


        startInfo.ArgumentList.Add(
            noiseScale.ToString(
                "0.###",
                CultureInfo.InvariantCulture));


        startInfo.ArgumentList.Add(
            "--noise_w");


        startInfo.ArgumentList.Add(
            noiseW.ToString(
                "0.###",
                CultureInfo.InvariantCulture));


        using Process process =
            new()
            {
                StartInfo =
                    startInfo
            };


        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start Piper.");
        }


        try
        {
            Task<string> outputTask =
                process
                    .StandardOutput
                    .ReadToEndAsync(
                        cancellationToken);


            Task<string> errorTask =
                process
                    .StandardError
                    .ReadToEndAsync(
                        cancellationToken);


            await process
                .StandardInput
                .WriteAsync(
                    text.AsMemory(),
                    cancellationToken);


            await process
                .StandardInput
                .FlushAsync(
                    cancellationToken);


            process
                .StandardInput
                .Close();


            await process
                .WaitForExitAsync(
                    cancellationToken);


            string output =
                await outputTask;


            string error =
                await errorTask;


            if (process.ExitCode !=
                0)
            {
                string detail =
                    string.IsNullOrWhiteSpace(
                        error)
                        ? string.Empty
                        : $" {error.Trim()}";


                throw new InvalidOperationException(
                    $"Piper exited with code " +
                    $"{process.ExitCode}.{detail}");
            }


            if (!File.Exists(
                    outputPath))
            {
                throw new InvalidOperationException(
                    "Piper completed without producing " +
                    "an audio file.");
            }


            if (!string.IsNullOrWhiteSpace(
                    output))
            {
                Debug.WriteLine(
                    $"[Piper] {output.Trim()}");
            }
        }
        catch
        {
            TryKill(
                process);


            throw;
        }
    }


    // =========================================================
    // VALIDATE
    // =========================================================

    private void ValidateFiles()
    {
        if (!File.Exists(
                _piperExecutable))
        {
            throw new FileNotFoundException(
                "Piper executable was not found.",
                _piperExecutable);
        }


        if (!File.Exists(
                _modelPath))
        {
            throw new FileNotFoundException(
                "Piper voice model was not found.",
                _modelPath);
        }
    }


    // =========================================================
    // KILL
    // =========================================================

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree:
                        true);
            }
        }
        catch
        {
        }
    }


    // =========================================================
    // TEMP FILE
    // =========================================================

    private static void DeleteTemporaryFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }


    // =========================================================
    // DISPOSE GUARD
    // =========================================================

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
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


        _speechLock.Dispose();
    }
}
