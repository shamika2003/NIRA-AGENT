/*
 * filename: MiniLmSemanticEncoder.cs
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SegaAgent.Semantic;

public sealed class MiniLmSemanticEncoder
    : ISegaSemanticEncoder,
      IDisposable
{
    private const string ModelDirectory =
        "all-MiniLM-L6-v2";


    private const int MaximumTokenCount =
        256;


    private const int CacheCapacity =
        128;


    private readonly object _sync =
        new();


    private readonly InferenceSession _session;

    private readonly SegaWordPieceTokenizer
        _tokenizer;


    private readonly bool _usesTokenTypeIds;

    private readonly string _outputName;

    private readonly SemanticOutputKind
        _outputKind;


    private readonly Dictionary<
        string,
        SemanticEmbedding>
        _cache =
            new(
                StringComparer.Ordinal);


    private readonly Queue<string>
        _cacheOrder =
            new();


    private bool _disposed;


    public MiniLmSemanticEncoder()
    {
        string directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Semantic",
                "Models",
                ModelDirectory);


        string vocabularyPath =
            Path.Combine(
                directory,
                "vocab.txt");


        string modelPath =
            ResolveModelPath(
                directory);


        if (!File.Exists(
                vocabularyPath))
        {
            throw new FileNotFoundException(
                "Sega semantic vocabulary was not found.",
                vocabularyPath);
        }


        _tokenizer =
            new SegaWordPieceTokenizer(
                vocabularyPath);


        SessionOptions options =
            new()
            {
                GraphOptimizationLevel =
                    GraphOptimizationLevel
                        .ORT_ENABLE_ALL,

                ExecutionMode =
                    ExecutionMode
                        .ORT_SEQUENTIAL,

                EnableCpuMemArena =
                    true,

                EnableMemoryPattern =
                    true
            };


        _session =
            new InferenceSession(
                modelPath,
                options);


        ValidateInputContract();


        _usesTokenTypeIds =
            _session
                .InputMetadata
                .ContainsKey(
                    "token_type_ids");


        (
            _outputName,
            _outputKind
        ) =
            ResolveOutputContract();


        Debug.WriteLine(
            $"[Semantic] MODEL LOADED | " +
            $"'{Path.GetFileName(modelPath)}' | " +
            $"Output='{_outputName}'");


        _ =
            Encode(
                "semantic warmup");
    }


    public SemanticEmbedding Encode(
        string text)
    {
        ThrowIfDisposed();


        if (string.IsNullOrWhiteSpace(
                text))
        {
            throw new ArgumentException(
                "Text cannot be empty.",
                nameof(text));
        }


        string cacheKey =
            text
                .Trim()
                .ToLowerInvariant();


        lock (_sync)
        {
            ThrowIfDisposed();


            if (_cache.TryGetValue(
                    cacheKey,
                    out SemanticEmbedding?
                        cached))
            {
                return cached;
            }


            Stopwatch stopwatch =
                Stopwatch.StartNew();


            SegaTokenizedInput tokenized =
                _tokenizer.Encode(
                    text,
                    MaximumTokenCount);


            int sequenceLength =
                tokenized
                    .InputIds
                    .Length;


            DenseTensor<long>
                inputIds =
                    new(
                        tokenized.InputIds,
                        new[]
                        {
                            1,
                            sequenceLength
                        });


            DenseTensor<long>
                attentionMask =
                    new(
                        tokenized.AttentionMask,
                        new[]
                        {
                            1,
                            sequenceLength
                        });


            List<NamedOnnxValue> inputs =
                new()
                {
                    NamedOnnxValue
                        .CreateFromTensor(
                            "input_ids",
                            inputIds),

                    NamedOnnxValue
                        .CreateFromTensor(
                            "attention_mask",
                            attentionMask)
                };


            if (_usesTokenTypeIds)
            {
                DenseTensor<long>
                    tokenTypeIds =
                        new(
                            tokenized.TokenTypeIds,
                            new[]
                            {
                                1,
                                sequenceLength
                            });


                inputs.Add(
                    NamedOnnxValue
                        .CreateFromTensor(
                            "token_type_ids",
                            tokenTypeIds));
            }


            using IDisposableReadOnlyCollection<
                DisposableNamedOnnxValue>
                outputs =
                    _session.Run(
                        inputs,
                        new[]
                        {
                            _outputName
                        });


            Tensor<float> output =
                outputs
                    .First()
                    .AsTensor<float>();


            float[] values =
                _outputKind switch
                {
                    SemanticOutputKind
                        .SentenceEmbedding =>
                            ReadSentenceEmbedding(
                                output),

                    SemanticOutputKind
                        .TokenEmbeddings =>
                            MeanPool(
                                output,
                                tokenized
                                    .AttentionMask),

                    _ =>
                        throw new InvalidOperationException(
                            "Unsupported semantic model output.")
                };


            NormalizeL2(
                values);


            SemanticEmbedding embedding =
                new(
                    values);


            AddToCache(
                cacheKey,
                embedding);


            stopwatch.Stop();


            Debug.WriteLine(
                $"[Semantic] ENCODE | " +
                $"Tokens={sequenceLength} | " +
                $"Dimensions={embedding.Dimension} | " +
                $"Time=" +
                $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms");


            return embedding;
        }
    }


    private static string ResolveModelPath(
        string directory)
    {
        string fullPrecision =
            Path.Combine(
                directory,
                "model.onnx");


        string avx2 =
            Path.Combine(
                directory,
                "model_quint8_avx2.onnx");


        string arm64 =
            Path.Combine(
                directory,
                "model_qint8_arm64.onnx");


        if (
            RuntimeInformation.ProcessArchitecture ==
                Architecture.Arm64
            &&
            File.Exists(
                arm64))
        {
            return arm64;
        }


        if (
            RuntimeInformation.ProcessArchitecture is
                Architecture.X64
                or Architecture.X86
            &&
            Avx2.IsSupported
            &&
            File.Exists(
                avx2))
        {
            return avx2;
        }


        if (File.Exists(
                fullPrecision))
        {
            return fullPrecision;
        }


        throw new FileNotFoundException(
            "No compatible Sega semantic ONNX model was found.",
            fullPrecision);
    }


    private void ValidateInputContract()
    {
        if (!_session
            .InputMetadata
            .ContainsKey(
                "input_ids"))
        {
            throw new InvalidOperationException(
                "Semantic model does not expose input_ids.");
        }


        if (!_session
            .InputMetadata
            .ContainsKey(
                "attention_mask"))
        {
            throw new InvalidOperationException(
                "Semantic model does not expose attention_mask.");
        }
    }


    private (
        string Name,
        SemanticOutputKind Kind
    )
        ResolveOutputContract()
    {
        if (_session
            .OutputMetadata
            .ContainsKey(
                "sentence_embedding"))
        {
            return (
                "sentence_embedding",
                SemanticOutputKind
                    .SentenceEmbedding
            );
        }


        if (_session
            .OutputMetadata
            .ContainsKey(
                "token_embeddings"))
        {
            return (
                "token_embeddings",
                SemanticOutputKind
                    .TokenEmbeddings
            );
        }


        if (_session
            .OutputMetadata
            .ContainsKey(
                "last_hidden_state"))
        {
            return (
                "last_hidden_state",
                SemanticOutputKind
                    .TokenEmbeddings
            );
        }


        throw new InvalidOperationException(
            "Semantic model does not expose a supported output.");
    }


    private static float[]
        ReadSentenceEmbedding(
            Tensor<float> output)
    {
        if (output.Rank !=
            2)
        {
            throw new InvalidOperationException(
                $"Expected rank-2 sentence embedding, " +
                $"received rank {output.Rank}.");
        }


        if (output.Dimensions[0] !=
            1)
        {
            throw new InvalidOperationException(
                "Semantic encoder expects batch size 1.");
        }


        int dimensions =
            output.Dimensions[1];


        float[] raw =
            output.ToArray();


        float[] result =
            new float[
                dimensions];


        Array.Copy(
            raw,
            result,
            dimensions);


        return result;
    }


    private static float[] MeanPool(
        Tensor<float> output,
        long[] attentionMask)
    {
        if (output.Rank !=
            3)
        {
            throw new InvalidOperationException(
                $"Expected rank-3 token embeddings, " +
                $"received rank {output.Rank}.");
        }


        int batch =
            output.Dimensions[0];


        int sequence =
            output.Dimensions[1];


        int dimensions =
            output.Dimensions[2];


        if (batch !=
            1)
        {
            throw new InvalidOperationException(
                "Semantic encoder expects batch size 1.");
        }


        if (sequence !=
            attentionMask.Length)
        {
            throw new InvalidOperationException(
                "Semantic output and attention mask " +
                "sequence lengths do not match.");
        }


        float[] raw =
            output.ToArray();


        float[] pooled =
            new float[
                dimensions];


        double count =
            0.0;


        for (
            int token = 0;
            token < sequence;
            token++)
        {
            if (attentionMask[token] ==
                0)
            {
                continue;
            }


            count +=
                1.0;


            int offset =
                token *
                dimensions;


            for (
                int dimension = 0;
                dimension < dimensions;
                dimension++)
            {
                pooled[dimension] +=
                    raw[
                        offset +
                        dimension];
            }
        }


        if (count <=
            0.0)
        {
            throw new InvalidOperationException(
                "Semantic encoder produced no active tokens.");
        }


        for (
            int dimension = 0;
            dimension < dimensions;
            dimension++)
        {
            pooled[dimension] =
                (float)(
                    pooled[dimension] /
                    count);
        }


        return pooled;
    }


    private static void NormalizeL2(
        float[] values)
    {
        double squareSum =
            0.0;


        foreach (
            float value
            in values)
        {
            squareSum +=
                value *
                value;
        }


        if (squareSum <=
            double.Epsilon)
        {
            throw new InvalidOperationException(
                "Semantic encoder produced a zero vector.");
        }


        double magnitude =
            Math.Sqrt(
                squareSum);


        for (
            int i = 0;
            i < values.Length;
            i++)
        {
            values[i] =
                (float)(
                    values[i] /
                    magnitude);
        }
    }


    private void AddToCache(
        string key,
        SemanticEmbedding embedding)
    {
        if (_cache.ContainsKey(
                key))
        {
            return;
        }


        while (_cache.Count >=
               CacheCapacity
               &&
               _cacheOrder.Count >
               0)
        {
            string oldest =
                _cacheOrder.Dequeue();


            _cache.Remove(
                oldest);
        }


        _cache[key] =
            embedding;


        _cacheOrder.Enqueue(
            key);
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }


        _disposed =
            true;


        _session.Dispose();
    }


    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(
                    MiniLmSemanticEncoder));
        }
    }


    private enum SemanticOutputKind
    {
        SentenceEmbedding,

        TokenEmbeddings
    }
}