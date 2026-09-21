/*
 * filename: GroqVocalDirectionMapper.cs
 */

using System.Diagnostics;

namespace NIRAAgent.Voice.Groq;

public static class GroqVocalDirectionMapper
{
    // =========================================================
    // MAP NIRA EXPRESSION TO ORPHEUS DIRECTION
    //
    // IMPORTANT:
    //
    // NIRA's psychology remains continuous and engine-neutral.
    //
    // This class is ONLY a provider adapter.
    //
    // Orpheus accepts natural-language vocal directions rather
    // than continuous acoustic vectors.
    // =========================================================

    public static string Map(
        NIRAVoiceExpression rawExpression)
    {
        NIRAVoiceExpression expression =
            rawExpression.Normalize();


        // =====================================================
        // CANDIDATE STRENGTHS
        // =====================================================

        double sarcastic =
            expression.Playfulness *
                0.42
            +
            expression.Irritation *
                0.38
            +
            expression.Confidence *
                0.20;


        double annoyed =
            expression.Irritation *
                0.65
            +
            expression.Tension *
                0.35;


        double warm =
            expression.Warmth *
                0.62
            +
            expression.Tenderness *
                0.38;


        double excited =
            expression.Arousal *
                0.52
            +
            Positive(
                expression.Valence) *
                0.22
            +
            expression.Playfulness *
                0.26;


        double confident =
            expression.Confidence *
                0.72
            +
            expression.Restraint *
                0.16
            +
            expression.Arousal *
                0.12;


        double breathy =
            expression.Tenderness *
                0.50
            +
            expression.Warmth *
                0.25
            +
            (
                1.0 -
                expression.Arousal
            ) *
                0.25;


        double calm =
            expression.Restraint *
                0.28
            +
            (
                1.0 -
                expression.Tension
            ) *
                0.26
            +
            (
                1.0 -
                expression.Irritation
            ) *
                0.24
            +
            (
                1.0 -
                expression.Arousal
            ) *
                0.22;


        // =====================================================
        // SPECIAL COMBINATION
        //
        // Playful irritation is very different from plain
        // irritation.
        // =====================================================

        if (
            expression.Irritation >=
                0.52
            &&
            expression.Playfulness >=
                0.42
            &&
            sarcastic >=
                0.58)
        {
            return Log(
                "sarcastic",
                sarcastic);
        }


        // =====================================================
        // FIND DOMINANT DELIVERY
        // =====================================================

        VocalCandidate[] candidates =
        [
            new(
                "annoyed",
                annoyed),

            new(
                "warm",
                warm),

            new(
                "excited",
                excited),

            new(
                "confidently",
                confident),

            new(
                "breathy",
                breathy),

            new(
                "calm",
                calm)
        ];


        VocalCandidate strongest =
            candidates
                .OrderByDescending(
                    candidate =>
                        candidate.Score)
                .First();


        // =====================================================
        // KEEP NORMAL SPEECH NATURAL
        //
        // Orpheus documentation specifically recommends fewer
        // directions for natural conversational delivery.
        // =====================================================

        if (strongest.Score <
            0.63)
        {
            return Log(
                string.Empty,
                strongest.Score);
        }


        return Log(
            strongest.Direction,
            strongest.Score);
    }


    // =========================================================
    // PROVIDER PREFIX
    // =========================================================

    public static string BuildPrefix(
        NIRAVoiceExpression expression)
    {
        string direction =
            Map(
                expression);


        if (string.IsNullOrWhiteSpace(
                direction))
        {
            return string.Empty;
        }


        return
            $"[{direction}] ";
    }


    // =========================================================
    // LOG
    // =========================================================

    private static string Log(
        string direction,
        double score)
    {
        Debug.WriteLine(
            $"[GroqVoiceDirection] " +
            $"Direction='" +
            $"{(
                string.IsNullOrWhiteSpace(
                    direction)
                    ? "natural"
                    : direction
            )}' | " +
            $"Strength={score:F2}");


        return direction;
    }


    private static double Positive(
        double value)
    {
        return Math.Max(
            0.0,
            value);
    }


    private readonly record struct VocalCandidate(
        string Direction,
        double Score);
}
