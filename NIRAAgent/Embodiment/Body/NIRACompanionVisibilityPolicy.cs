namespace NIRAAgent.Embodiment.Body;

/// <summary>
/// One owner setting controls the companion's physical WPF window everywhere.
/// A chat dock is allowed only while the actual chat workspace is active.
/// This policy is deliberately model-free and has no side effects.
/// </summary>
public static class NIRACompanionVisibilityPolicy
{
    public static bool CanDock(bool presenceEnabled, bool mainWindowActive,
        bool chatWorkspaceVisible, bool sidebarExpanded) =>
        presenceEnabled && mainWindowActive && chatWorkspaceVisible && sidebarExpanded;

    public static bool CanShow(bool presenceEnabled, bool mainWindowActive,
        bool chatWorkspaceVisible, bool sidebarExpanded) =>
        presenceEnabled && (!mainWindowActive ||
            CanDock(presenceEnabled, mainWindowActive, chatWorkspaceVisible, sidebarExpanded));
}
