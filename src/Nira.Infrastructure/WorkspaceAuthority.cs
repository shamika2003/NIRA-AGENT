using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Grantless operations are confined to a dedicated workspace. External writes,
/// sensitive reads and dangerous actions receive a per-operation user prompt.
/// This policy cannot be overridden through retrieved webpage or tool text.
/// </summary>
public sealed class WorkspaceAuthority(string workspaceRoot, bool developmentMode = false) : IToolAuthorization
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));

    // Explicitly enumerated public network capabilities, not a blanket external grant.
    // New external tools and private-file transfers remain approval-gated.
    private static readonly HashSet<string> DevelopmentPublicTools = new(StringComparer.Ordinal)
    {
        "web.search", "video.search", "image.search", "image.retrieve", "image.generate",
        "browser.inspect_public", "browser.follow_public_link", "browser.session_open",
        "browser.session_inspect", "browser.session_inspect_controls", "browser.session_inspect_links",
        "browser.session_inspect_text", "browser.session_follow", "browser.session_follow_named_link"
    };

    public bool IsPreapproved(ToolDescriptor tool, string target)
    {
        if (developmentMode)
        {
            // File/document mutations, arbitrary commands, UI changes and private uploads
            // never acquire authority from the debugging convenience policy.
            if (tool.Risk is ToolRisk.Dangerous or ToolRisk.WriteWorkspace or ToolRisk.WriteOutsideWorkspace)
                return false;
            if (tool.Risk == ToolRisk.ReadExternal) return DevelopmentPublicTools.Contains(tool.Name);
            if (tool.Risk == ToolRisk.ReadLocal)
                return !tool.HasSideEffects || tool.Name == "browser.session_close";
            return false;
        }
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
        (developmentMode
            ? "Development mode still requires approval for this protected operation. Approve this specific action only?"
            : $"The preapproved workspace is '{_root}'. Approve this specific action only?");
}
