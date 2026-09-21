/*
 * filename: NIRAWordPieceTokenizer.cs
 */

using System.Globalization;
using System.Text;

namespace NIRAAgent.Semantic;

internal sealed class NIRAWordPieceTokenizer
{
    private const int MaximumWordCharacters =
        100;


    private readonly Dictionary<
        string,
        long>
        _vocabulary;


    private readonly long _unknownId;

    private readonly long _clsId;

    private readonly long _sepId;


    public NIRAWordPieceTokenizer(
        string vocabularyPath)
    {
        if (!File.Exists(
                vocabularyPath))
        {
            throw new FileNotFoundException(
                "Semantic tokenizer vocabulary was not found.",
                vocabularyPath);
        }


        string[] lines =
            File.ReadAllLines(
                vocabularyPath);


        _vocabulary =
            new Dictionary<
                string,
                long>(
                    lines.Length,
                    StringComparer.Ordinal);


        for (
            int i = 0;
            i < lines.Length;
            i++)
        {
            string token =
                lines[i];


            if (i == 0)
            {
                token =
                    token.TrimStart(
                        '\uFEFF');
            }


            if (!_vocabulary.ContainsKey(
                    token))
            {
                _vocabulary[token] =
                    i;
            }
        }


        _unknownId =
            GetRequiredTokenId(
                "[UNK]");


        _clsId =
            GetRequiredTokenId(
                "[CLS]");


        _sepId =
            GetRequiredTokenId(
                "[SEP]");
    }


    public NIRATokenizedInput Encode(
        string text,
        int maximumTokenCount)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            throw new ArgumentException(
                "Text cannot be empty.",
                nameof(text));
        }


        if (maximumTokenCount < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTokenCount));
        }


        List<string> basicTokens =
            BasicTokenize(
                text);


        List<long> ids =
            new(
                Math.Min(
                    maximumTokenCount,
                    64));


        ids.Add(
            _clsId);


        foreach (
            string token
            in basicTokens)
        {
            IReadOnlyList<long> pieces =
                WordPiece(
                    token);


            foreach (
                long piece
                in pieces)
            {
                if (ids.Count >=
                    maximumTokenCount - 1)
                {
                    break;
                }


                ids.Add(
                    piece);
            }


            if (ids.Count >=
                maximumTokenCount - 1)
            {
                break;
            }
        }


        ids.Add(
            _sepId);


        long[] inputIds =
            ids.ToArray();


        long[] attentionMask =
            new long[
                inputIds.Length];


        long[] tokenTypeIds =
            new long[
                inputIds.Length];


        Array.Fill(
            attentionMask,
            1L);


        return new NIRATokenizedInput(
            inputIds,
            attentionMask,
            tokenTypeIds);
    }


    private List<string> BasicTokenize(
        string text)
    {
        string normalized =
            text.Normalize(
                NormalizationForm.FormD);


        List<string> tokens =
            new();


        StringBuilder current =
            new();


        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }


            tokens.Add(
                current.ToString());


            current.Clear();
        }


        foreach (
            char original
            in normalized)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        original);


            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }


            char character =
                char.ToLowerInvariant(
                    original);


            if (char.IsWhiteSpace(
                    character)
                ||
                char.IsControl(
                    character))
            {
                Flush();

                continue;
            }


            if (IsCjk(
                    character)
                ||
                IsPunctuation(
                    character))
            {
                Flush();


                tokens.Add(
                    character.ToString());


                continue;
            }


            current.Append(
                character);
        }


        Flush();


        return tokens;
    }


    private IReadOnlyList<long> WordPiece(
        string token)
    {
        if (string.IsNullOrEmpty(
                token))
        {
            return Array.Empty<long>();
        }


        if (token.Length >
            MaximumWordCharacters)
        {
            return new[]
            {
                _unknownId
            };
        }


        List<long> pieces =
            new();


        int start =
            0;


        bool failed =
            false;


        while (start <
               token.Length)
        {
            int end =
                token.Length;


            long foundId =
                -1;


            int foundEnd =
                -1;


            while (start <
                   end)
            {
                string piece =
                    token[
                        start..end];


                if (start >
                    0)
                {
                    piece =
                        "##" +
                        piece;
                }


                if (_vocabulary
                    .TryGetValue(
                        piece,
                        out long id))
                {
                    foundId =
                        id;


                    foundEnd =
                        end;


                    break;
                }


                end--;
            }


            if (foundId <
                0)
            {
                failed =
                    true;

                break;
            }


            pieces.Add(
                foundId);


            start =
                foundEnd;
        }


        if (failed)
        {
            return new[]
            {
                _unknownId
            };
        }


        return pieces;
    }


    private long GetRequiredTokenId(
        string token)
    {
        if (_vocabulary.TryGetValue(
                token,
                out long id))
        {
            return id;
        }


        throw new InvalidOperationException(
            $"Required semantic tokenizer token " +
            $"was not found: {token}");
    }


    private static bool IsPunctuation(
        char character)
    {
        int code =
            character;


        if (
            code is >= 33 and <= 47
            ||
            code is >= 58 and <= 64
            ||
            code is >= 91 and <= 96
            ||
            code is >= 123 and <= 126)
        {
            return true;
        }


        UnicodeCategory category =
            CharUnicodeInfo
                .GetUnicodeCategory(
                    character);


        return category is
            UnicodeCategory
                .ConnectorPunctuation
            or UnicodeCategory
                .DashPunctuation
            or UnicodeCategory
                .OpenPunctuation
            or UnicodeCategory
                .ClosePunctuation
            or UnicodeCategory
                .InitialQuotePunctuation
            or UnicodeCategory
                .FinalQuotePunctuation
            or UnicodeCategory
                .OtherPunctuation;
    }


    private static bool IsCjk(
        char character)
    {
        int code =
            character;


        return
            code is >= 0x4E00
                and <= 0x9FFF
            ||
            code is >= 0x3400
                and <= 0x4DBF
            ||
            code is >= 0x3040
                and <= 0x30FF
            ||
            code is >= 0xAC00
                and <= 0xD7AF;
    }
}


internal readonly record struct NIRATokenizedInput(
    long[] InputIds,
    long[] AttentionMask,
    long[] TokenTypeIds);
