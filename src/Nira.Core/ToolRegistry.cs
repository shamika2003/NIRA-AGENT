using System.Text.Json;

namespace Nira.Core;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    // The composition root defines ordered starter capabilities. This is catalog
    // configuration, not task classification, execution authority, or a required workflow.
    // Optional providers may be absent; the remaining priorities keep their order.
    public ToolRegistry(IEnumerable<IAgentTool> tools, IEnumerable<string>? defaultCapabilityOrder = null,
        IReadOnlyDictionary<string, string[]>? relatedCapabilities = null)
    {
        foreach (IAgentTool tool in tools)
        {
            if (!_tools.TryAdd(tool.Descriptor.Name, tool))
                throw new AgentException($"Duplicate tool name: {tool.Descriptor.Name}");
        }
        DefaultDescriptors = Array.AsReadOnly((defaultCapabilityOrder ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).Where(_tools.ContainsKey)
            .Select(name => _tools[name].Descriptor).ToArray());
        _related = (relatedCapabilities ?? new Dictionary<string, string[]>())
            .Where(pair => _tools.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Where(_tools.ContainsKey)
                .Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    private readonly Dictionary<string, string[]> _related;
    public IReadOnlyList<string> RelatedCapabilities(string name) =>
        _related.TryGetValue(name, out var related) ? Array.AsReadOnly(related) : Array.Empty<string>();

    public IReadOnlyList<ToolDescriptor> DefaultDescriptors { get; }

    public IReadOnlyList<ToolDescriptor> Descriptors =>
        _tools.Values.Select(t => t.Descriptor).ToArray();

    // Grammatical stop words only; no task or application vocabulary.
    private static readonly HashSet<string> CommonRoutingWords = new(StringComparer.Ordinal)
    {
        "about", "also", "could", "does", "from", "have", "into", "please", "should", "some",
        "that", "their", "them", "then", "there", "these", "they", "this", "those", "through",
        "using", "very", "want", "what", "when", "where", "which", "while", "will", "with", "would", "your"
    };

    // Local lexical retrieval over registered capability metadata. This only affects
    // the initial schema shortlist; the model remains the planner and all execution
    // requires the normal schema, observation and authority checks. No task-specific
    // intent rules, application names or provider calls are involved.
    public IReadOnlyList<string> RankInitialCapabilities(string request)
    {
        static HashSet<string> Terms(string text)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string token in text.ToLowerInvariant().Split(
                text.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 4 || !token.Any(char.IsLetter) || CommonRoutingWords.Contains(token)) continue;
                words.Add(token.Length > 4 && token.EndsWith('s') ? token[..^1] : token);
            }
            return words;
        }
        var query = Terms(request[..Math.Min(request.Length, 2000)]);
        if (query.Count < 2) return [];
        var catalog = Descriptors.Select(d => (Descriptor: d, Terms: Terms(d.Name + " " + d.Description))).ToArray();
        if (catalog.Length == 0) return [];
        static string Family(string name) => name.Split('.')[0];
        var families = catalog.GroupBy(d => Family(d.Descriptor.Name), StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Terms: g.SelectMany(d => d.Terms).ToHashSet(StringComparer.Ordinal))).ToArray();
        var familyWeights = query.ToDictionary(term => term, term => Math.Log((families.Length + 1d) /
            (1d + families.Count(f => f.Terms.Contains(term)))), StringComparer.Ordinal);
        // Different steps in one request may match different tools in the same family.
        // Rank their combined metadata instead of requiring one descriptor to match every step.
        var matchingFamilies = families.Select(f => (f.Name, Hits: f.Terms.Count(w => query.Contains(w) && familyWeights[w] >= 0.65),
            Score: f.Terms.Where(query.Contains).Sum(w => familyWeights[w])))
            .Where(f => f.Hits >= 2).OrderByDescending(f => f.Score).ThenBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (matchingFamilies.Length == 0) return [];
        string family = matchingFamilies[0].Name;
        var weights = query.ToDictionary(term => term, term => Math.Log((catalog.Length + 1d) /
            (1d + catalog.Count(d => d.Terms.Contains(term)))), StringComparer.Ordinal);
        var ranked = catalog.Where(d => Family(d.Descriptor.Name) == family)
            .Select(d => (d.Descriptor, Score: d.Terms.Where(query.Contains).Sum(w => weights[w])))
            .Where(d => d.Score > 0).OrderByDescending(d => d.Score).ThenBy(d => d.Descriptor.Name, StringComparer.Ordinal).ToArray();
        // Keep actual registered family entry points and the strongest matching schemas,
        // then fill from that family. Reserve room for ordinary starter capabilities.
        return DefaultDescriptors.Where(d => Family(d.Name) == family).Take(2).Select(d => d.Name)
            .Concat(ranked.Take(3).Select(d => d.Descriptor.Name))
            .Concat(catalog.Where(d => Family(d.Descriptor.Name) == family).Select(d => d.Descriptor.Name))
            .Distinct(StringComparer.Ordinal).Take(6).ToArray();
    }

    public bool TryGet(string name, out IAgentTool? tool) => _tools.TryGetValue(name, out tool);

    public bool Validate(string name, string argumentJson, out JsonElement arguments, out string error)
    {
        arguments = default;
        if (!TryGet(name, out var tool) || tool is null)
        { error = "Tool not registered: " + name; return false; }
        try
        {
            using var document = JsonDocument.Parse(argumentJson);
            arguments = document.RootElement.Clone();
        }
        catch (JsonException e) { error = "Invalid JSON arguments: " + e.Message; return false; }
        if (tool is IToolArgumentNormalizer normalizer)
        {
            if (!JsonToolSchema.HasUniqueProperties(arguments, "$", out error)) return false;
            try { arguments = normalizer.NormalizeArguments(arguments); }
            catch (ArgumentException e) { error = e.Message; return false; }
        }
        return JsonToolSchema.Validate(tool.Descriptor.Parameters, arguments, out error);
    }
}
