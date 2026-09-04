/*
 * filename: SpeechChunker.cs
 */

using System.Diagnostics;
using System.Text;

namespace SegaAgent.Voice;

public sealed class SpeechChunker
{
    // =========================================================
    // STREAMING SPEECH BATCHING
    //
    // IMPORTANT:
    //
    // This is no longer a "one sentence = one TTS request"
    // segmenter.
    //
    // Natural punctuation remains inside a larger speech batch
    // so the voice engine itself controls normal pauses.
    //
    // We still emit before the entire LLM response is complete
    // when enough text has accumulated.
    // =========================================================

    /*
     * Wait until roughly this much speech exists before we
     * consider emitting a streaming batch.
     *
     * Short normal replies therefore normally become ONE
     * continuous voice request.
     */
    private const int PreferredBatchCharacters =
        160;


    /*
     * Groq Orpheus currently has a small input limit.
     *
     * Leave room for:
     *
     * [calm]
     * [warm]
     * [confidently]
     * [sarcastic]
     *
     * which GroqVocalDirectionMapper adds later.
     */
    private const int MaximumBatchCharacters =
        184;


    /*
     * Avoid creating tiny first batches merely because the
     * model happened to produce a period early.
     */
    private const int MinimumNaturalBatchCharacters =
        90;


    /*
     * Before reaching the hard limit, only emit at punctuation
     * when that punctuation is near the END of the accumulated
     * text.
     *
     * Example:
     *
     * "Hey, I'm good. I figured you were testing the limits."
     *
     * should remain one batch rather than:
     *
     * "Hey, I'm good."
     *
     * +
     *
     * "I figured..."
     */
    private const int EarlyBoundaryWindow =
        24;


    // =========================================================
    // BUFFER
    // =========================================================

    private readonly StringBuilder
        _buffer =
            new();


    // =========================================================
    // ADD STREAMING TEXT
    // =========================================================

    public IReadOnlyList<string> Add(
        string text)
    {
        List<string> batches =
            new();


        if (string.IsNullOrEmpty(
                text))
        {
            return batches;
        }


        _buffer.Append(
            text);


        while (true)
        {
            int batchLength =
                FindReadyBatchLength(
                    _buffer);


            if (batchLength <=
                0)
            {
                break;
            }


            string rawBatch =
                _buffer.ToString(
                    0,
                    batchLength);


            _buffer.Remove(
                0,
                batchLength);


            string batch =
                SpeechTextSanitizer
                    .Sanitize(
                        rawBatch);


            if (!ContainsSpeakableContent(
                    batch))
            {
                continue;
            }


            Debug.WriteLine(
                $"[SpeechBatch] " +
                $"Streaming | " +
                $"Characters={batch.Length}");


            batches.Add(
                batch);
        }


        return batches;
    }


    // =========================================================
    // COMPLETE RESPONSE
    //
    // Once Ollama has finished, whatever remains belongs to the
    // final natural speech batch.
    //
    // A short response therefore usually reaches Groq as ONE
    // complete utterance.
    // =========================================================

    public string? Complete()
    {
        string rawRemaining =
            _buffer.ToString();


        _buffer.Clear();


        string remaining =
            SpeechTextSanitizer
                .Sanitize(
                    rawRemaining);


        if (!ContainsSpeakableContent(
                remaining))
        {
            return null;
        }


        Debug.WriteLine(
            $"[SpeechBatch] " +
            $"Final | " +
            $"Characters={remaining.Length}");


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
    // READY BATCH
    // =========================================================

    private static int FindReadyBatchLength(
        StringBuilder buffer)
    {
        if (buffer.Length <
            PreferredBatchCharacters)
        {
            return -1;
        }


        int limit =
            Math.Min(
                buffer.Length,
                MaximumBatchCharacters);


        // =====================================================
        // BELOW HARD LIMIT
        //
        // We already have enough text for speech, but don't
        // arbitrarily cut the sentence.
        //
        // Only emit if a natural sentence boundary occurs near
        // the end.
        // =====================================================

        if (buffer.Length <
            MaximumBatchCharacters)
        {
            int minimumBoundary =
                Math.Max(
                    MinimumNaturalBatchCharacters,
                    limit -
                    EarlyBoundaryWindow);


            return FindSentenceBoundaryLength(
                buffer,
                minimumBoundary,
                limit);
        }


        // =====================================================
        // HARD LIMIT REACHED
        //
        // We now need to emit something.
        //
        // Prefer a real sentence boundary.
        // =====================================================

        int sentenceSearchStart =
            Math.Max(
                MinimumNaturalBatchCharacters,
                limit -
                70);


        int sentenceBoundary =
            FindSentenceBoundaryLength(
                buffer,
                sentenceSearchStart,
                limit);


        if (sentenceBoundary >
            0)
        {
            return sentenceBoundary;
        }


        // =====================================================
        // NO SENTENCE BOUNDARY NEAR LIMIT
        //
        // Prefer a softer punctuation boundary.
        // =====================================================

        int softBoundary =
            FindSoftBoundaryLength(
                buffer,
                Math.Max(
                    MinimumNaturalBatchCharacters,
                    limit -
                    45),
                limit);


        if (softBoundary >
            0)
        {
            return softBoundary;
        }


        // =====================================================
        // LAST NATURAL OPTION: WHITESPACE
        // =====================================================

        int whitespaceBoundary =
            FindWhitespaceBoundaryLength(
                buffer,
                MinimumNaturalBatchCharacters,
                limit);


        if (whitespaceBoundary >
            0)
        {
            return whitespaceBoundary;
        }


        // =====================================================
        // EXTREMELY LONG UNBROKEN TOKEN
        //
        // Last resort only.
        // =====================================================

        return limit;
    }


    // =========================================================
    // SENTENCE BOUNDARY
    //
    // Search backwards so we choose the LATEST useful sentence
    // boundary rather than the first period encountered.
    // =========================================================

    private static int FindSentenceBoundaryLength(
        StringBuilder buffer,
        int minimumIndex,
        int maximumExclusive)
    {
        int maximumIndex =
            Math.Min(
                maximumExclusive,
                buffer.Length)
            -
            1;


        for (
            int index =
                maximumIndex;

            index >=
                minimumIndex;

            index--)
        {
            char character =
                buffer[index];


            if (!IsTerminalPunctuation(
                    character))
            {
                continue;
            }


            // =================================================
            // DECIMAL
            //
            // 3.14
            // =================================================

            if (
                character ==
                    '.'
                &&
                IsDecimalPoint(
                    buffer,
                    index))
            {
                continue;
            }


            // =================================================
            // INITIAL / ABBREVIATION STRUCTURE
            //
            // Avoid obvious cases such as:
            //
            // e.g.
            // i.e.
            // U.S.
            //
            // This is structural only, not a hard-coded word
            // list.
            // =================================================

            if (
                character ==
                    '.'
                &&
                LooksLikeInitialSequence(
                    buffer,
                    index))
            {
                continue;
            }


            int end =
                index;


            // =================================================
            // ABSORB:
            //
            // ...
            // ?!
            // !!
            // ???
            // =================================================

            while (
                end +
                    1 <
                    maximumExclusive
                &&
                end +
                    1 <
                    buffer.Length
                &&
                IsTerminalPunctuation(
                    buffer[
                        end +
                        1]))
            {
                end++;
            }


            // =================================================
            // ABSORB CLOSING QUOTES / BRACKETS
            //
            // "Seriously?"
            //
            // should NOT become:
            //
            // "Seriously?
            //
            // followed by a separate quote.
            // =================================================

            while (
                end +
                    1 <
                    maximumExclusive
                &&
                end +
                    1 <
                    buffer.Length
                &&
                IsClosingCharacter(
                    buffer[
                        end +
                        1]))
            {
                end++;
            }


            return end +
                1;
        }


        return -1;
    }


    // =========================================================
    // SOFT BOUNDARY
    // =========================================================

    private static int FindSoftBoundaryLength(
        StringBuilder buffer,
        int minimumIndex,
        int maximumExclusive)
    {
        int maximumIndex =
            Math.Min(
                maximumExclusive,
                buffer.Length)
            -
            1;


        for (
            int index =
                maximumIndex;

            index >=
                minimumIndex;

            index--)
        {
            char character =
                buffer[index];


            if (
                character ==
                    ','
                ||
                character ==
                    ';'
                ||
                character ==
                    ':'
                ||
                character ==
                    '—')
            {
                return index +
                    1;
            }
        }


        return -1;
    }


    // =========================================================
    // WHITESPACE BOUNDARY
    // =========================================================

    private static int FindWhitespaceBoundaryLength(
        StringBuilder buffer,
        int minimumIndex,
        int maximumExclusive)
    {
        int maximumIndex =
            Math.Min(
                maximumExclusive,
                buffer.Length)
            -
            1;


        for (
            int index =
                maximumIndex;

            index >=
                minimumIndex;

            index--)
        {
            if (char.IsWhiteSpace(
                    buffer[index]))
            {
                return index +
                    1;
            }
        }


        return -1;
    }


    // =========================================================
    // TERMINAL PUNCTUATION
    // =========================================================

    private static bool IsTerminalPunctuation(
        char character)
    {
        return
            character ==
                '.'
            ||
            character ==
                '!'
            ||
            character ==
                '?';
    }


    // =========================================================
    // CLOSING CHARACTER
    // =========================================================

    private static bool IsClosingCharacter(
        char character)
    {
        return character switch
        {
            '"' =>
                true,

            '\'' =>
                true,

            '”' =>
                true,

            '’' =>
                true,

            ')' =>
                true,

            ']' =>
                true,

            '}' =>
                true,

            _ =>
                false
        };
    }


    // =========================================================
    // DECIMAL POINT
    // =========================================================

    private static bool IsDecimalPoint(
        StringBuilder buffer,
        int index)
    {
        if (
            index <=
                0
            ||
            index +
                1 >=
                buffer.Length)
        {
            return false;
        }


        return
            char.IsDigit(
                buffer[
                    index -
                    1])
            &&
            char.IsDigit(
                buffer[
                    index +
                    1]);
    }


    // =========================================================
    // INITIAL / ABBREVIATION STRUCTURE
    // =========================================================

    private static bool LooksLikeInitialSequence(
        StringBuilder buffer,
        int periodIndex)
    {
        if (periodIndex <=
            0)
        {
            return false;
        }


        if (!char.IsLetter(
                buffer[
                    periodIndex -
                    1]))
        {
            return false;
        }


        // =====================================================
        // e.g
        // i.e
        // U.S
        // =====================================================

        if (
            periodIndex +
                1 <
                buffer.Length
            &&
            char.IsLetter(
                buffer[
                    periodIndex +
                    1]))
        {
            return true;
        }


        // =====================================================
        // Second period in:
        //
        // e.g.
        // U.S.
        //
        // Look backwards for another period inside a very short
        // token.
        // =====================================================

        int searchStart =
            Math.Max(
                0,
                periodIndex -
                4);


        for (
            int index =
                periodIndex -
                    1;

            index >=
                searchStart;

            index--)
        {
            char character =
                buffer[index];


            if (character ==
                '.')
            {
                return true;
            }


            if (char.IsWhiteSpace(
                    character))
            {
                break;
            }
        }


        return false;
    }


    // =========================================================
    // SPEAKABLE CONTENT
    //
    // Also acts as the provider-level safety function used by
    // Groq.
    // =========================================================

    public static bool ContainsSpeakableContent(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return false;
        }


        foreach (
            char character
            in text)
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                return true;
            }
        }


        return false;
    }
}