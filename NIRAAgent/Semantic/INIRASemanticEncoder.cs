/*
 * filename: INIRASemanticEncoder.cs
 */

namespace NIRAAgent.Semantic;

public interface INIRASemanticEncoder
{
    SemanticEmbedding Encode(
        string text);
}
