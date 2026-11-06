using System.Text.Json;
using System.Text.RegularExpressions;
using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.General;

/// <summary>
/// A capability declares its provider schema and host-side argument validator.
/// Models never register capabilities. This is the common extension seam for PC,
/// browser, document, research and other NIRA capabilities.
/// </summary>
public interface IGeneralCapability : IAgentTool
{
    string FunctionName { get; }
    JsonElement InputSchema { get; }
    bool ValidateArguments(JsonElement arguments);
}

public sealed class CapabilityRegistry
{
    private static readonly Regex FunctionPattern = new("^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
    private readonly IReadOnlyDictionary<string, IGeneralCapability> _byId;
    private readonly IReadOnlyDictionary<string, IGeneralCapability> _byFunction;

    public CapabilityRegistry(IEnumerable<IGeneralCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var ids = new Dictionary<string, IGeneralCapability>(StringComparer.Ordinal);
        var functions = new Dictionary<string, IGeneralCapability>(StringComparer.Ordinal);
        foreach (IGeneralCapability capability in capabilities)
        {
            if (capability is null || string.IsNullOrWhiteSpace(capability.Spec.Id) ||
                capability.Spec.Id.Length > 64 || string.IsNullOrWhiteSpace(capability.Spec.Description) ||
                capability.Spec.Description.Length > 800 || !FunctionPattern.IsMatch(capability.FunctionName) ||
                capability.InputSchema.ValueKind != JsonValueKind.Object ||
                !capability.InputSchema.TryGetProperty("type", out JsonElement type) ||
                (type.ValueKind != JsonValueKind.String || type.GetString() != "object") ||
                capability.InputSchema.GetRawText().Length > 8192 ||
                !ids.TryAdd(capability.Spec.Id, capability) ||
                !functions.TryAdd(capability.FunctionName, capability))
                throw new ArgumentException("Duplicate or invalid capability descriptor.", nameof(capabilities));
        }
        if (ids.Count > 24) throw new ArgumentException("Capability context limit exceeded.", nameof(capabilities));
        _byId = ids;
        _byFunction = functions;
    }

    public IReadOnlyList<IAgentTool> Tools => _byId.Values.Cast<IAgentTool>().ToArray();
    public int Count => _byId.Count;

    public bool TryGetFunction(string name, out IGeneralCapability capability)
    {
        if (_byFunction.TryGetValue(name, out IGeneralCapability? found))
        {
            capability = found;
            return true;
        }
        capability = null!;
        return false;
    }

    /// <summary>Only schemas belonging to real engine-advertised capabilities appear.</summary>
    public object[] BuildProviderTools(IReadOnlyList<AgentToolSpec> available, bool allowWrite = false)
    {
        ArgumentNullException.ThrowIfNull(available);
        var schemas = new List<object>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (AgentToolSpec spec in available)
        {
            if (spec is null || !unique.Add(spec.Id) ||
                !_byId.TryGetValue(spec.Id, out IGeneralCapability? capability) ||
                spec != capability.Spec)
                throw new InvalidOperationException("Tool registry does not match the runtime.");
            if (spec.Access != AccessLevel.Observe && !allowWrite)
                throw new InvalidOperationException("Mutating tools need a separate approved execution profile.");
            schemas.Add(new { type = "function", function = new {
                name = capability.FunctionName, description = spec.Description,
                parameters = capability.InputSchema } });
        }
        return schemas.ToArray();
    }
}

/// <summary>Reuses the already-tested Tavily tool, exposing its validated query shape.</summary>
public sealed class SearchCapability : IGeneralCapability
{
    private readonly Nira.V2.Web.TavilySearchTool _inner;
    public SearchCapability(Nira.V2.Web.TavilySearchTool inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public AgentToolSpec Spec => _inner.Spec;
    public string FunctionName => "web_search";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
        type = "object", properties = new { query = new { type = "string",
            description = "Focused public query (3-240 characters)" } },
        required = new[] { "query" }, additionalProperties = false });
    public bool ValidateArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.EnumerateObject().Count() == 1 &&
        arguments.TryGetProperty("query", out JsonElement query) &&
        query.ValueKind == JsonValueKind.String &&
        query.GetString() is string q && q.Trim().Length is >= 3 and <= 240 &&
        !q.Any(char.IsControl);
    public Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct) =>
        _inner.ExecuteAsync(reference, ct);
}
