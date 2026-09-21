/*
 * filename: SpeechTextSanitizer.cs
 */

using System.Text.RegularExpressions;

namespace NIRAAgent.Voice;

public static partial class SpeechTextSanitizer
{
    // =========================================================
    // SANITIZE
    // =========================================================

    public static string Sanitize(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return string.Empty;
        }


        string result =
            text;


        // =====================================================
        // MARKDOWN IMAGES
        //
        // ![description](url)
        // ->
        // description
        // =====================================================

        result =
            MarkdownImageRegex()
                .Replace(
                    result,
                    "$1");


        // =====================================================
        // MARKDOWN LINKS
        //
        // [OpenAI](https://...)
        // ->
        // OpenAI
        // =====================================================

        result =
            MarkdownLinkRegex()
                .Replace(
                    result,
                    "$1");


        // =====================================================
        // RAW URLS
        //
        // Raw URLs are poor speech content.
        // =====================================================

        result =
            UrlRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // CODE FENCES
        // =====================================================

        result =
            CodeFenceRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // HEADINGS
        //
        // ### Personality
        // ->
        // Personality
        // =====================================================

        result =
            HeadingRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // BLOCK QUOTES
        // =====================================================

        result =
            BlockQuoteRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // BULLETS / NUMBERED LIST MARKERS
        // =====================================================

        result =
            ListMarkerRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // MARKDOWN EMPHASIS / INLINE CODE
        // =====================================================

        result =
            result
                .Replace(
                    "**",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "__",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "~~",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "`",
                    string.Empty,
                    StringComparison.Ordinal);


        // =====================================================
        // SINGLE EMPHASIS MARKERS
        // =====================================================

        result =
            StandaloneFormattingRegex()
                .Replace(
                    result,
                    string.Empty);


        // =====================================================
        // TABLE SEPARATORS
        // =====================================================

        result =
            result.Replace(
                '|',
                ' ');


        // =====================================================
        // WHITESPACE
        // =====================================================

        result =
            WhitespaceRegex()
                .Replace(
                    result,
                    " ")
                .Trim();


        return result;
    }


    // =========================================================
    // REGEX
    // =========================================================

    [GeneratedRegex(
        @"!\[([^\]]*)\]\([^)]+\)",
        RegexOptions.Compiled)]
    private static partial Regex
        MarkdownImageRegex();


    [GeneratedRegex(
        @"\[([^\]]+)\]\([^)]+\)",
        RegexOptions.Compiled)]
    private static partial Regex
        MarkdownLinkRegex();


    [GeneratedRegex(
        @"https?://\S+",
        RegexOptions.IgnoreCase |
        RegexOptions.Compiled)]
    private static partial Regex
        UrlRegex();


    [GeneratedRegex(
        @"(?m)^\s*```[^\r\n]*\s*$",
        RegexOptions.Compiled)]
    private static partial Regex
        CodeFenceRegex();


    [GeneratedRegex(
        @"(?m)^\s{0,3}#{1,6}\s*",
        RegexOptions.Compiled)]
    private static partial Regex
        HeadingRegex();


    [GeneratedRegex(
        @"(?m)^\s*>\s?",
        RegexOptions.Compiled)]
    private static partial Regex
        BlockQuoteRegex();


    [GeneratedRegex(
        @"(?m)^\s*(?:(?:[-+*])|(?:\d+[.)]))\s+",
        RegexOptions.Compiled)]
    private static partial Regex
        ListMarkerRegex();


    [GeneratedRegex(
        @"(?<!\w)[*_~](?!\w)|(?<=\s)[*_~](?=\S)|(?<=\S)[*_~](?=\s)",
        RegexOptions.Compiled)]
    private static partial Regex
        StandaloneFormattingRegex();


    [GeneratedRegex(
        @"\s+",
        RegexOptions.Compiled)]
    private static partial Regex
        WhitespaceRegex();
}
