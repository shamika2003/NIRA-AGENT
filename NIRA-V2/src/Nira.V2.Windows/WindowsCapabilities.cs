using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.Windows;

/// <summary>
/// Host-owned registration gate. The model cannot turn PC observation on.
/// Explicit session opt-in allows process NAMES only, not filenames or titles.
/// </summary>
public static class WindowsObservationRegistration
{
    public const string ConsentValue = "ALLOW_APP_NAMES";
    public static bool IsConsented(string? value) =>
        string.Equals(value, ConsentValue, StringComparison.Ordinal);

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        string? sessionConsent, IWindowsDesktopObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        if (!IsConsented(sessionConsent)) return Array.Empty<IGeneralCapability>();
        if (observer is null && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows desktop observation requires Windows.");
        observer ??= new Win32DesktopObserver();
        return new IGeneralCapability[]
        {
            new VisibleApplicationsCapability(vault, observer),
            new ForegroundApplicationCapability(vault, observer)
        };
    }
}

/// <summary>Common strict schema and host-side sanitation for Window observations.</summary>
public abstract class WindowsObservationCapability : IGeneralCapability
{
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
    protected readonly AgentPayloadVault Vault;
    private readonly IWindowsDesktopObserver _observer;
    protected WindowsObservationCapability(AgentPayloadVault vault, IWindowsDesktopObserver observer)
    {
        Vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public abstract AgentToolSpec Spec { get; }
    public abstract string FunctionName { get; }
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object && !arguments.EnumerateObject().Any();

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        JsonElement arguments;
        try
        {
            runId = Vault.RunIdFromArgument(reference);
            arguments = await Vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        {
            return new AgentToolReceipt(false, false, null, "INVALID_SCOPED_ARGUMENT");
        }
        if (!ValidateArguments(arguments))
            return new AgentToolReceipt(false, false, null, "INVALID_WINDOWS_ARGUMENTS");
        DesktopWindowSnapshot snapshot;
        try { snapshot = await _observer.ObserveAsync(ct); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or
            ExternalException or PlatformNotSupportedException)
        {
            return new AgentToolReceipt(false, false, null, "WINDOWS_OBSERVATION_UNAVAILABLE");
        }
        if (snapshot is null || snapshot.VisibleApplications is null)
            return new AgentToolReceipt(false, false, null, "WINDOWS_OBSERVATION_UNAVAILABLE");
        string[] safeUniqueNames = snapshot.VisibleApplications
            .Where(n => n is not null && n.Length <= 64 && SafeName.IsMatch(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool rejectedUnsafeNames = snapshot.VisibleApplications.Any(n =>
            n is null || n.Length > 64 || !SafeName.IsMatch(n));
        string[] names = safeUniqueNames.Take(24).ToArray();
        string? foreground = snapshot.ForegroundApplication;
        if (foreground is null || foreground.Length > 64 || !SafeName.IsMatch(foreground))
            foreground = null;
        // Never serialize full raw snapshots from a probe. Serialize only safe fields.
        JsonElement result = CreateOutput(snapshot.ObservedUtc.ToUniversalTime(),
            names, foreground, Math.Clamp(snapshot.ObservedVisibleWindows, 0, 256),
            snapshot.Truncated || safeUniqueNames.Length > 24 || rejectedUnsafeNames ||
            snapshot.ObservedVisibleWindows > 256);
        string token = await Vault.SaveResultAsync(runId, Spec.Id, result, ct);
        return AgentToolReceipt.ReadOnlyResult(token);
    }

    protected abstract JsonElement CreateOutput(DateTimeOffset observedUtc, string[] apps,
        string? foreground, int visibleWindows, bool truncated);
}

public sealed class VisibleApplicationsCapability : WindowsObservationCapability
{
    public VisibleApplicationsCapability(AgentPayloadVault vault, IWindowsDesktopObserver observer)
        : base(vault, observer) { }
    public override AgentToolSpec Spec { get; } = new("windows.visible_apps",
        "Observe only the process names of visible top-level Windows applications. " +
        "No window titles, paths, document names, screenshots or file contents. " +
        "Requires explicit host opt-in; returned process names are shared with the model provider.",
        AccessLevel.Observe);
    public override string FunctionName => "windows_visible_apps";
    protected override JsonElement CreateOutput(DateTimeOffset observedUtc, string[] apps,
        string? foreground, int visibleWindows, bool truncated) =>
        JsonSerializer.SerializeToElement(new
        {
            observation = "visible_application_process_names_only",
            observedUtc,
            processNames = apps,
            observedVisibleWindows = visibleWindows,
            truncated,
            excluded = "window_titles,file_paths,documents,screen_pixels,command_lines,window_handles"
        });
}

public sealed class ForegroundApplicationCapability : WindowsObservationCapability
{
    public ForegroundApplicationCapability(AgentPayloadVault vault, IWindowsDesktopObserver observer)
        : base(vault, observer) { }
    public override AgentToolSpec Spec { get; } = new("windows.foreground_app",
        "Observe the current foreground application's process name, if available. " +
        "Never retrieves a window title, path, document or screenshot. Requires host opt-in.",
        AccessLevel.Observe);
    public override string FunctionName => "windows_foreground_app";
    protected override JsonElement CreateOutput(DateTimeOffset observedUtc, string[] apps,
        string? foreground, int visibleWindows, bool truncated) =>
        JsonSerializer.SerializeToElement(new
        {
            observation = "foreground_application_process_name_only",
            observedUtc,
            foregroundProcessName = foreground ?? "unavailable",
            excluded = "window_titles,file_paths,documents,screen_pixels,command_lines,window_handles"
        });
}
