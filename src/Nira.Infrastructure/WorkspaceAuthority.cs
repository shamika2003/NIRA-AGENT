using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Grantless operations are confined to a dedicated workspace. External writes,
/// sensitive reads and dangerous actions receive a per-operation user prompt.
/// This policy cannot be overridden through retrieved webpage or tool text.
/// </summary>
public sealed class WorkspaceAuthority(string workspaceRoot) : IToolAuthorization
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));

    public bool IsPreapproved(ToolDescriptor tool, string target)
    {
        if (tool.Risk == ToolRisk.ReadExternal) return false; // explicit network-query consent
        if (tool.Risk == ToolRisk.Dangerous) return false;
        if (target == "workspace") return true;
        if (!Path.IsPathFullyQualified(target)) return false;
        string absolute = Path.GetFullPath(target);
        bool inside = absolute.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
            absolute.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return inside && tool.Risk is ToolRisk.ReadLocal or ToolRisk.WriteWorkspace;
    }

    public string ExplainScope(ToolDescriptor tool, string target) =>
        $"Tool '{tool.Name}' requests {tool.Risk} access to '{target}'. " +
        $"The preapproved workspace is '{_root}'. Approve this specific action only?";
}
