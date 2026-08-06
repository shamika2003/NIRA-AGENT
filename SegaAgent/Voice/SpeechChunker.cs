/*
 * filename: SpeechChunker.cs
 */

namespace SegaAgent.Voice;

public sealed class SpeechChunker
{
    private readonly System.Text.StringBuilder _buffer = new();

    // =========================================================
    // ADD STREAMING TEXT
    // =========================================================

    public IReadOnlyList<string> Add(
        string text)
    {
        var sentences =
            new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            return sentences;
        }

        _buffer.Append(text);

        while (true)
        {
            var boundary =
                FindSentenceBoundary(
                    _buffer
                );

            if (boundary < 0)
            {
                break;
            }

            var length =
                boundary + 1;

            var sentence =
                _buffer
                    .ToString(
                        0,
                        length
                    )
                    .Trim();

            _buffer.Remove(
                0,
                length
            );

            if (!string.IsNullOrWhiteSpace(
                    sentence))
            {
                sentences.Add(sentence);
            }
        }

        return sentences;
    }


    // =========================================================
    // COMPLETE
    //
    // Call this when Ollama finishes streaming.
    //
    // This returns whatever text remains in the buffer.
    // =========================================================

    public string? Complete()
    {
        var remaining =
            _buffer
                .ToString()
                .Trim();

        _buffer.Clear();

        if (string.IsNullOrWhiteSpace(
                remaining))
        {
            return null;
        }

        return remaining;
    }


    // =========================================================
    // CLEAR
    // =========================================================

    public void Clear()
    {
        _buffer.Clear();
    }


    // =========================================================
    // SENTENCE BOUNDARY
    // =========================================================

    private static int FindSentenceBoundary(
        System.Text.StringBuilder buffer)
    {
        for (
            var i = 0;
            i < buffer.Length;
            i++)
        {
            var character =
                buffer[i];

            if (character != '.' &&
                character != '!' &&
                character != '?' &&
                character != '\n')
            {
                continue;
            }

            // ---------------------------------------------
            // Avoid breaking decimal numbers.
            //
            // Example:
            //
            // 3.14
            // ---------------------------------------------

            if (character == '.' &&
                i > 0 &&
                i + 1 < buffer.Length &&
                char.IsDigit(buffer[i - 1]) &&
                char.IsDigit(buffer[i + 1]))
            {
                continue;
            }

            return i;
        }

        return -1;
    }
}