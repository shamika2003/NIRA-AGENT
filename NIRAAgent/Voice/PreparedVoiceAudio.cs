/*
 * filename: PreparedVoiceAudio.cs
 */

namespace NIRAAgent.Voice;

public sealed class PreparedVoiceAudio
    : IDisposable
{
    // =========================================================
    // SOURCE
    // =========================================================

    public VoiceUtterance Utterance
    {
        get;
    }


    // =========================================================
    // ENGINE
    // =========================================================

    public string Engine
    {
        get;
    }


    // =========================================================
    // AUDIO
    //
    // Normally this contains one WAV.
    //
    // Multiple files are supported defensively in case a
    // provider has to split one utterance because of its own
    // hard input limit.
    // =========================================================

    public IReadOnlyList<string> AudioPaths
    {
        get;
    }


    // =========================================================
    // DISPOSE
    // =========================================================

    private int
        _disposed;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PreparedVoiceAudio(
        VoiceUtterance utterance,
        IEnumerable<string> audioPaths,
        string engine)
    {
        ArgumentNullException.ThrowIfNull(
            utterance);


        ArgumentNullException.ThrowIfNull(
            audioPaths);


        ArgumentException
            .ThrowIfNullOrWhiteSpace(
                engine);


        string[] paths =
            audioPaths
                .Where(
                    path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .ToArray();


        if (paths.Length ==
            0)
        {
            throw new ArgumentException(
                "Prepared voice audio must contain " +
                "at least one audio file.",
                nameof(audioPaths));
        }


        Utterance =
            utterance;


        AudioPaths =
            paths;


        Engine =
            engine.Trim();
    }


    // =========================================================
    // DISPOSE
    //
    // PreparedVoiceAudio owns all temporary audio files.
    // =========================================================

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref _disposed,
                1) !=
            0)
        {
            return;
        }


        foreach (
            string path
            in AudioPaths)
        {
            DeleteTemporaryFile(
                path);
        }
    }


    // =========================================================
    // DELETE
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
            /*
             * Temporary-file cleanup must never crash NIRA.
             */
        }
    }
}
