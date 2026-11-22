using System.Text.Json;

namespace Nira.Core;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        foreach (IAgentTool tool in tools)
        {
            if (!_tools.TryAdd(tool.Descriptor.Name, tool))
                throw new AgentException($"Duplicate tool name: {tool.Descriptor.Name}");
        }
    }

    public IReadOnlyList<ToolDescriptor> Descriptors =>
        _tools.Values.Select(t => t.Descriptor).ToArray();

    public bool TryGet(string name, out IAgentTool? tool) => _tools.TryGetValue(name, out tool);

    public bool Validate(string name, string argumentJson, out JsonElement arguments, out string error)
    {
        arguments = default;
        if (!TryGet(name, out var tool) || tool is null)
        { error = "Tool not registered: " + name; return false; }
        try { arguments = JsonDocument.Parse(argumentJson).RootElement.Clone(); }
        catch (JsonException e) { error = "Invalid JSON arguments: " + e.Message; return false; }
        return JsonToolSchema.Validate(tool.Descriptor.Parameters, arguments, out error);
    }
}
