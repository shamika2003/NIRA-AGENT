/*
 * filename: SemanticEmbedding.cs
 */

namespace SegaAgent.Semantic;

public sealed class SemanticEmbedding
{
    private readonly float[] _values;


    public SemanticEmbedding(
        float[] values)
    {
        ArgumentNullException.ThrowIfNull(
            values);


        if (values.Length == 0)
        {
            throw new ArgumentException(
                "Semantic embedding cannot be empty.",
                nameof(values));
        }


        _values =
            values.ToArray();
    }


    public int Dimension =>
        _values.Length;


    public ReadOnlyMemory<float> Values =>
        _values;


    internal ReadOnlySpan<float> Span =>
        _values;
}