/*
 * filename: ISegaSemanticEncoder.cs
 */

namespace SegaAgent.Semantic;

public interface ISegaSemanticEncoder
{
    SemanticEmbedding Encode(
        string text);
}