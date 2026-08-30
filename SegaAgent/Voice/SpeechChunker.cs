/*
 * filename: SpeechChunker.cs
 */

using System.Text;

namespace SegaAgent.Voice;

public sealed class SpeechChunker
{
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
        List<string> speechParts =
            new();


        if (string.IsNullOrEmpty(
                text))
        {
            return speechParts;
        }


        _buffer.Append(
            text);


        while (true)
        {
            int boundary =
                FindSentenceBoundary(
                    _buffer);


            if (boundary <
                0)
            {
                break;
            }


            int length =
                boundary +
                1;


            string rawPart =
                _buffer
                    .ToString(
                        0,
                        length);


            _buffer.Remove(
                0,
                length);


            string speechPart =
                SpeechTextSanitizer
                    .Sanitize(
                        rawPart);


            if (!string.IsNullOrWhiteSpace(
                    speechPart))
            {
                speechParts.Add(
                    speechPart);
            }
        }


        return speechParts;
    }


    // =========================================================
    // COMPLETE
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
        StringBuilder buffer)
    {
        for (
            int i = 0;
            i < buffer.Length;
            i++)
        {
            char character =
                buffer[i];


            if (
                character != '.'
                &&
                character != '!'
                &&
                character != '?'
                &&
                character != '\n')
            {
                continue;
            }


            // =================================================
            // DECIMAL NUMBER
            //
            // 3.14
            // =================================================

            if (
                character ==
                    '.'
                &&
                i >
                    0
                &&
                i +
                    1 <
                    buffer.Length
                &&
                char.IsDigit(
                    buffer[
                        i -
                        1])
                &&
                char.IsDigit(
                    buffer[
                        i +
                        1]))
            {
                continue;
            }


            // =================================================
            // COMMON ABBREVIATION / INITIAL
            //
            // Avoid splitting:
            //
            // e.g.
            // i.e.
            // U.S.
            //
            // when another period follows very closely.
            // =================================================

            if (
                character ==
                    '.'
                &&
                LooksLikeAbbreviation(
                    buffer,
                    i))
            {
                continue;
            }


            return i;
        }


        return -1;
    }


    // =========================================================
    // ABBREVIATION CHECK
    // =========================================================

    private static bool LooksLikeAbbreviation(
        StringBuilder buffer,
        int periodIndex)
    {
        if (periodIndex <=
            0)
        {
            return false;
        }


        char previous =
            buffer[
                periodIndex -
                1];


        if (!char.IsLetter(
                previous))
        {
            return false;
        }


        /*
         * Single-letter initial:
         *
         * U.S.
         *
         * First period should not terminate speech.
         */

        if (
            periodIndex +
                2 <
                buffer.Length
            &&
            char.IsLetter(
                buffer[
                    periodIndex +
                    1])
            &&
            buffer[
                periodIndex +
                2] ==
                '.')
        {
            return true;
        }


        /*
         * Short abbreviation fragment:
         *
         * e.g.
         * i.e.
         */

        int start =
            periodIndex -
            1;


        while (
            start >=
                0
            &&
            (
                char.IsLetter(
                    buffer[start])
                ||
                buffer[start] ==
                    '.'
            ))
        {
            start--;
        }


        int length =
            periodIndex -
            start;


        if (
            length <=
                4
            &&
            start +
                1 <
                periodIndex
            &&
            buffer
                .ToString(
                    start +
                        1,
                    length)
                .Contains(
                    '.',
                    StringComparison.Ordinal))
        {
            return true;
        }


        return false;
    }
}