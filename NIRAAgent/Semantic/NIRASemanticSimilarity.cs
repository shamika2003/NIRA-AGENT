/*
 * filename: NIRASemanticSimilarity.cs
 */

namespace NIRAAgent.Semantic;

public static class NIRASemanticSimilarity
{
    public static double Cosine(
        SemanticEmbedding first,
        SemanticEmbedding second)
    {
        ArgumentNullException.ThrowIfNull(
            first);


        ArgumentNullException.ThrowIfNull(
            second);


        if (first.Dimension !=
            second.Dimension)
        {
            throw new ArgumentException(
                "Semantic embedding dimensions do not match.");
        }


        ReadOnlySpan<float> a =
            first.Span;


        ReadOnlySpan<float> b =
            second.Span;


        double dot =
            0.0;


        double normA =
            0.0;


        double normB =
            0.0;


        for (
            int i = 0;
            i < a.Length;
            i++)
        {
            double firstValue =
                a[i];


            double secondValue =
                b[i];


            dot +=
                firstValue *
                secondValue;


            normA +=
                firstValue *
                firstValue;


            normB +=
                secondValue *
                secondValue;
        }


        if (normA <=
                double.Epsilon
            ||
            normB <=
                double.Epsilon)
        {
            return 0.0;
        }


        return Math.Clamp(
            dot /
            (
                Math.Sqrt(
                    normA)
                *
                Math.Sqrt(
                    normB)
            ),
            -1.0,
            1.0);
    }
}
