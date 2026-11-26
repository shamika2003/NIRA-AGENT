using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

public static class NiraStandardTools
{
    public static ToolRegistry Create(string root, SqliteConversationStore store, HttpClient http,
        IPublicBrowserSessions? browserSessions = null, IDesktopAutomationBackend? desktopBackend = null,
        VisionProvider? vision = null, ImageGenerationProvider? imageGeneration = null,
        Func<ImageGenerationProvider?>? imageGenerationResolver = null,
        IApplicationDesktopBackend? applicationBackend = null, IAgentTool? proceduralRecall = null)
    {
        Directory.CreateDirectory(root);
        var resolver = new WorkspacePathResolver(root);
        browserSessions ??= new EdgePublicBrowserSessions();
        desktopBackend ??= new WindowsDesktopAutomationBackend();
        applicationBackend ??= new WindowsApplicationDesktopBackend();
        return new ToolRegistry([
            new WorkspaceInfoTool(resolver), new SystemInspectTool(), new FileListTool(resolver),
            new FileReadTool(resolver), new FileWriteTool(resolver),
            new FileFindTool(resolver), new FileHashTool(resolver),
            new ProductivityInspectTool(resolver), new ProductivityReadTool(resolver),
            new ProductivityCreateWorkbookTool(resolver), new ProductivityCreateDocumentTool(resolver),
            new ProductivityReviseWorkbookTool(resolver), new ProductivityReviseDocumentTool(resolver),
            new PdfExportTextTool(resolver), new PdfSearchTool(resolver),
            new OcrReadTool(resolver),
            ..(vision is null ? Array.Empty<IAgentTool>() : new IAgentTool[] { new VisionAnalyzeTool(resolver, http, vision) }),
            new DirectoryCreateTool(resolver), new FileEditTextTool(resolver),
            new ProcessRunTool(resolver),
            new ApplicationFindTool(applicationBackend), new ApplicationLaunchTool(applicationBackend),
            new DesktopWindowsTool(desktopBackend), new DesktopInspectTool(desktopBackend),
            ..(desktopBackend is IDesktopWindowCaptureBackend capture
                ? new IAgentTool[] { new DesktopWindowCaptureTool(resolver, capture) } : Array.Empty<IAgentTool>()),
            new DesktopActivateTool(desktopBackend), new DesktopInvokeTool(desktopBackend), new DesktopControlSequenceTool(desktopBackend),
            new DesktopSetTextTool(desktopBackend), new DesktopTypeTextTool(desktopBackend),
            new PublicBrowserInspectionTool(),
            new PublicBrowserLinkTool(),
            new BrowserSessionOpenTool(browserSessions), new BrowserSessionInspectTool(browserSessions),
            new BrowserSessionInspectControlsTool(browserSessions),
            new BrowserSessionInspectLinksTool(browserSessions),
            new BrowserSessionInspectVisibleTextTool(browserSessions),
            new BrowserSessionFollowTool(browserSessions), new BrowserSessionSemanticLinkTool(browserSessions),
            new BrowserSessionCloseTool(browserSessions),
            new BrowserSessionFillTextTool(browserSessions),
            new BrowserSessionClickButtonTool(browserSessions),
            new BrowserSessionSemanticActionTool(browserSessions),
            new BrowserSessionSelectOptionTool(browserSessions),
            new BrowserSessionSetCheckedTool(browserSessions),
            ..(proceduralRecall is null ? Array.Empty<IAgentTool>() : new IAgentTool[] { proceduralRecall }),
            new MemorySearchTool(store), new MemoryRememberTool(store), new MemoryManageTool(store), new MemoryHistorySearchTool(store),
            new TavilySearchTool(http), new VideoSearchTool(http), new ImageDiscoveryTool(http), new ImagePreviewTool(resolver),
            ..(imageGenerationResolver is not null
                ? new IAgentTool[] { new ImageGenerationTool(resolver, http, imageGenerationResolver) }
                : imageGeneration is null ? Array.Empty<IAgentTool>()
                : new IAgentTool[] { new ImageGenerationTool(resolver, http, imageGeneration) })
        ], defaultCapabilityOrder: [
            // Keep established cross-domain entry points ahead of optional extensions.
            // Additional registered capabilities remain discoverable and selectable.
            "image.generate", "video.search", "web.search", "system.inspect",
            "memory.search", "workspace.info", "filesystem.find", "filesystem.read_text", "filesystem.list",
            "productivity.inspect", "desktop.application_find", "desktop.windows_list"
        ], relatedCapabilities: new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Registered evidence/workflow relationships, not user-task classifiers.
            ["web.search"] = ["image.search", "browser.inspect_public", "image.retrieve"],
            ["browser.inspect_public"] = ["web.search", "image.search", "image.retrieve"],
            ["image.search"] = ["image.retrieve", "web.search", "browser.inspect_public"],
            ["image.retrieve"] = ["image.search", "web.search", "browser.inspect_public"]
        });
    }
}

public sealed class WorkspacePathResolver(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required; inspect workspace.info or ask the user.");
        string trimmed = path.Trim();
        // Reject unresolved placeholders generically, not only for Word documents.
        if ((trimmed.Contains('[') && trimmed.Contains(']')) ||
            (trimmed.Contains('{') && trimmed.Contains('}')))
            throw new ArgumentException("The path contains an unresolved placeholder. Use workspace.info or ask for a real destination.");
        string resolved = Path.GetFullPath(Path.IsPathFullyQualified(trimmed) ? trimmed : Path.Combine(Root, trimmed));
        // Reparse points (including Windows junctions and symbolic links) can
        // redirect a lexically in-scope path elsewhere. Fail closed until a
        // handle-based safe traversal is implemented.
        for (string? segment = resolved; segment is not null; segment = Path.GetDirectoryName(segment))
        {
            if (!File.Exists(segment) && !Directory.Exists(segment)) continue;
            if ((File.GetAttributes(segment) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("This path contains a symbolic link or junction; use a real directory path.");
        }
        return resolved;
    }
}

internal static class ToolSchemas
{
    public static JsonElement New(string json) => JsonToolSchema.Schema(json);
    public static JsonElement GetArgs(JsonElement args, string key) => args.GetProperty(key);
    public static string String(JsonElement args, string key) => args.GetProperty(key).GetString()!;
}

public sealed class WorkspaceInfoTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("workspace.info",
        "Get the current permitted workspace and available default output location.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{},"additionalProperties":false}
        """));
    public string GetTarget(JsonElement arguments) => "workspace";
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        Task.FromResult(ToolReceipt.Ok("Current workspace", new { path = paths.Root,
            writable = Directory.Exists(paths.Root) }));
}

/// <summary>Read-only local Windows snapshot. No shell, network, identifiers or user files.</summary>
public sealed class SystemInspectTool : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("system.inspect",
        "Inspect this PC directly: Windows, CPU, RAM, optional running processes by RAM, registered applications and PATH commands, storage and display hardware. Set includeProcesses or includeApplications only when needed. No shell commands; read-only.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{"includeStorage":{"type":"boolean","description":"Include fixed-drive capacity."},"includeHardware":{"type":"boolean","description":"Include motherboard and display adapters."},"includeProcesses":{"type":"boolean","description":"Include current process memory ranking."},"includeApplications":{"type":"boolean","description":"Include registered applications and optional PATH lookup. Defaults to false."},"applicationNameContains":{"type":"string","maxLength":100,"description":"Optional legacy single name filter; empty means no filter. Ignored unless includeApplications is true."},"applicationQueries":{"type":"array","items":{"type":"string","maxLength":100},"description":"Optional 1–8 product names or command basenames, matched independently; empty means unfiltered inventory. Only used when includeApplications is true."}},"additionalProperties":false}
        """));
    public string GetTarget(JsonElement arguments) => "local computer (read-only system information)";
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!JsonToolSchema.Validate(Descriptor.Parameters, args, out string error))
            return Task.FromResult(ToolReceipt.Fail("ARGUMENT_SCHEMA", error));
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(ToolReceipt.Fail("SYSTEM_PLATFORM_UNSUPPORTED", "This system inspection currently supports Windows."));
        string? processor = null;
        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
            processor = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();
            if (processor is { Length: > 200 }) processor = processor[..200];
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        bool includeHardware = Requested(args, "includeHardware");
        bool includeProcesses = Requested(args, "includeProcesses");
        bool includeApplications = Requested(args, "includeApplications");
        string? applicationFilter = includeApplications && args.TryGetProperty("applicationNameContains", out var filter) ? filter.GetString()?.Trim() : null;
        string[] applicationQueries = [];
        if (includeApplications)
        {
            var names = args.TryGetProperty("applicationQueries", out var supplied) ? supplied : default;
            if (names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 8)
                return Task.FromResult(ToolReceipt.Fail("ARGUMENT_SCHEMA", "Supply at most eight application names or command basenames."));
            applicationQueries = (names.ValueKind == JsonValueKind.Array ? names.EnumerateArray().Select(v => v.GetString()!.Trim()) : Enumerable.Empty<string>())
                .Append(applicationFilter ?? "").Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (applicationQueries.Length > 8 || applicationQueries.Any(name => name.Any(char.IsControl)))
                return Task.FromResult(ToolReceipt.Fail("ARGUMENT_SCHEMA", "Use at most eight application names without control characters."));
        }
        string? installationType = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType");
        string? edition = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID");
        string? displayVersion = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
        Version version = Environment.OSVersion.Version;
        string? osFamily = WindowsFamily(version, installationType);
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool memoryAvailable = GlobalMemoryStatusEx(ref memory);
        bool includeStorage = args.TryGetProperty("includeStorage", out var storage) && storage.GetBoolean();
        var drives = new List<object>();
        bool storageIncomplete = false;
        if (includeStorage)
        {
            try
            {
                var fixedDrives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).ToArray();
                storageIncomplete = fixedDrives.Length > 26;
                foreach (var drive in fixedDrives.Take(26))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (!drive.IsReady) { storageIncomplete = true; continue; }
                        drives.Add(new { drive = drive.Name, totalBytes = drive.TotalSize, availableBytes = drive.AvailableFreeSpace });
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { storageIncomplete = true; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { storageIncomplete = true; }
        }
        return Task.FromResult(ToolReceipt.Ok("Observed local system information", new
        {
            observedUtc = DateTimeOffset.UtcNow,
            osVersion = Environment.OSVersion.VersionString,
            windowsFamily = osFamily,
            windowsBuild = version.Build,
            windowsEditionId = edition,
            windowsDisplayVersion = displayVersion,
            windowsInstallationType = installationType,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            processorName = processor,
            logicalProcessorsAvailableToProcess = Environment.ProcessorCount,
            physicalProcessorCores = ReadPhysicalCoreCount(),
            totalPhysicalMemoryBytes = memoryAvailable ? (ulong?)memory.TotalPhys : null,
            availablePhysicalMemoryBytes = memoryAvailable ? (ulong?)memory.AvailPhys : null,
            totalPhysicalMemoryGiB = memoryAvailable ? (double?)Math.Round(memory.TotalPhys / 1073741824d, 2) : null,
            availablePhysicalMemoryGiB = memoryAvailable ? (double?)Math.Round(memory.AvailPhys / 1073741824d, 2) : null,
            memoryLoadPercent = memoryAvailable ? (uint?)memory.MemoryLoad : null,
            uptimeSeconds = Environment.TickCount64 / 1000,
            storageRequested = includeStorage,
            storageIncomplete,
            fixedDrives = drives,
            hardware = includeHardware ? Hardware(ct) : null,
            processes = includeProcesses ? Processes(ct) : null,
            applications = includeApplications ? WindowsApplicationInventory.Inspect(applicationQueries, ct) : null,
            limitations = "Null optional sections were not requested. Missing fields are unknown. OS family is derived from the Windows client/server installation type and build; RAM GiB uses 1024-based units. No temperatures, health, GPU utilization, per-process CPU utilization or benchmarks are measured. Drive capacity does not identify drive speed."
        }));
    }

    private static bool Requested(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.GetBoolean();

    public static string? WindowsFamily(Version version, string? installationType)
    {
        if (installationType is null) return null;
        if (installationType.StartsWith("Server", StringComparison.OrdinalIgnoreCase)) return "Windows Server";
        if (!installationType.Equals("Client", StringComparison.OrdinalIgnoreCase)) return null;
        if (version.Major == 10) return version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return null;
    }

    private static string? Bounded(string? value, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = new(value.Trim().Where(c => !char.IsControl(c)).Take(limit).ToArray());
        return text.Length == 0 ? null : text;
    }

    private static string? ReadRegistry(string path, string name)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(path, writable: false);
            return Bounded(key?.GetValue(name) as string);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { return null; }
    }

    private static object Hardware(CancellationToken ct)
    {
        const string bios = @"HARDWARE\DESCRIPTION\System\BIOS";
        var adapters = new List<object>();
        bool incomplete = false;
        for (uint i = 0; i < 32; i++)
        {
            ct.ThrowIfCancellationRequested();
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            // Windows display enumeration includes virtual and disconnected entries. Do not imply physical presence.
            const uint attached = 0x1, primary = 0x4, mirroring = 0x8;
            string? name = Bounded(device.Description);
            if (name is not null && (device.Flags & mirroring) == 0)
                adapters.Add(new { name, attachedToDesktop = (device.Flags & attached) != 0, primaryDisplay = (device.Flags & primary) != 0 });
            if (i == 31) incomplete = true;
        }
        return new
        {
            systemManufacturer = ReadRegistry(bios, "SystemManufacturer"),
            systemProductName = ReadRegistry(bios, "SystemProductName"),
            motherboardManufacturer = ReadRegistry(bios, "BaseBoardManufacturer"),
            motherboardProduct = ReadRegistry(bios, "BaseBoardProduct"),
            displayAdapters = adapters, incomplete,
            limitations = "Display devices reported by Windows, including possible virtual/disconnected entries; not an exhaustive physical GPU inventory. No serial numbers, VRAM, utilization or benchmark measured."
        };
    }

    private static object Processes(CancellationToken ct)
    {
        var rows = new List<(string Name, int Id, long WorkingSet)>();
        int unavailable = 0;
        Process[] running;
        try { running = Process.GetProcesses(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return new { unavailable = true, limitations = "Windows process enumeration was unavailable." }; }
        try
        {
            foreach (Process process in running.Take(4096))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string? name = Bounded(process.ProcessName);
                    if (name is not null) rows.Add((name, process.Id, process.WorkingSet64));
                    else unavailable++;
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { unavailable++; }
            }
            return new
            {
                observedProcessCount = running.Length, unreadableCount = unavailable,
                truncated = running.Length > 4096 || rows.Count > 30,
                returnedProcessCount = Math.Min(rows.Count, 30),
                topByWorkingSet = rows.OrderByDescending(p => p.WorkingSet).Take(30)
                    .Select(p => new { name = p.Name, pid = p.Id, workingSetBytes = p.WorkingSet, workingSetMiB = Math.Round(p.WorkingSet / 1048576d, 1) }).ToArray(),
                limitations = "Instantaneous per-process working set, including shared pages; do not sum as unique RAM or infer CPU load. Memory compression does not establish paging activity, a threshold, a leak or the cause of slowness. No arguments, executable paths, window titles or process contents read."
            };
        }
        finally { foreach (Process process in running) process.Dispose(); }
    }

    // RelationProcessorCore is zero. Count records rather than assuming core/thread ratios.
    private static int? ReadPhysicalCoreCount()
    {
        uint size = 0;
        _ = GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref size);
        if (size is < 8 or > 1048576) return null;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint capacity = size;
            if (!GetLogicalProcessorInformationEx(0, buffer, ref size) || size > capacity) return null;
            int count = 0, offset = 0;
            while (offset < size)
            {
                if (size - offset < 8) return null;
                int relationship = Marshal.ReadInt32(buffer, offset);
                int recordSize = Marshal.ReadInt32(buffer, offset + 4);
                if (relationship != 0 || recordSize < 8 || recordSize > size - offset) return null;
                count++;
                offset += recordSize;
            }
            return count > 0 ? count : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnedLength);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

public sealed class FileListTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.list",
        "List directory entries and metadata. Relative paths are resolved inside NIRA's workspace.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1}},
         "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args,"path"));
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        if (!Directory.Exists(path)) return Task.FromResult(ToolReceipt.Fail("DIRECTORY_NOT_FOUND", path));
        var entries = Directory.EnumerateFileSystemEntries(path).Take(120).Select(p => new {
            name = Path.GetFileName(p), directory = Directory.Exists(p),
            bytes = File.Exists(p) ? new FileInfo(p).Length : (long?)null
        }).ToArray();
        return Task.FromResult(ToolReceipt.Ok($"Listed {entries.Length} entries", new { path, entries }));
    }
}

public sealed class FileReadTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.read_text",
        "Read a bounded UTF-8 text file, not arbitrary binary or credentials.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1}},
         "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args,"path"));
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        if (!File.Exists(path)) return ToolReceipt.Fail("FILE_NOT_FOUND", "The file does not exist", new { path });
        var info = new FileInfo(path);
        if (info.Length > 256 * 1024) return ToolReceipt.Fail("FILE_TOO_LARGE", "Use a specialized bounded reader", new { bytes = info.Length });
        string content = await File.ReadAllTextAsync(path, ct);
        return ToolReceipt.Ok("Read file", new { path, content, length = content.Length });
    }
}

public sealed class FileWriteTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.write_text",
        "Create a new UTF-8 text file without overwriting existing data. Use workspace.info if no destination is given.",
        ToolRisk.WriteWorkspace, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096},"content":{"type":"string","maxLength":200000}},
         "required":["path","content"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args,"path"));
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        string content = ToolSchemas.String(args, "content");
        if (File.Exists(path) || Directory.Exists(path))
            return ToolReceipt.Fail("ALREADY_EXISTS", "No overwrite is allowed", new { path });
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return ToolReceipt.Fail("PARENT_NOT_FOUND", "Parent directory is missing. Ask for a directory or use an existing workspace directory.", new { path, parent });
        string temp = Path.Combine(parent, ".nira-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct);
            // Atomic same-volume publication, no overwrite.
            File.Move(temp, path, overwrite: false);
            string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))).ToLowerInvariant();
            return ToolReceipt.Ok("Created and independently read back new file", new {
                path, sha256 = hash, characters = content.Length }, changedState: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class FileFindTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.find",
        "Search file names recursively within a specified directory, bounded to 150 matches.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096},"nameContains":{"type":"string","minLength":1,"maxLength":200}},
         "required":["path","nameContains"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args,"path"));
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        if (!Directory.Exists(path)) return Task.FromResult(ToolReceipt.Fail("DIRECTORY_NOT_FOUND", path));
        string pattern = ToolSchemas.String(args, "nameContains");
        try
        {
            var matches = new List<string>();
            var pending = new Queue<string>();
            pending.Enqueue(path);
            int directoriesVisited = 0;
            while (pending.Count > 0 && directoriesVisited < 1500 && matches.Count < 150)
            {
                ct.ThrowIfCancellationRequested();
                string current = pending.Dequeue();
                directoriesVisited++;
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    if (Path.GetFileName(file).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(file);
                        if (matches.Count == 150) break;
                    }
                }
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                        pending.Enqueue(directory);
                }
            }
            return Task.FromResult(ToolReceipt.Ok("File name search", new {
                path, matches, directoriesVisited, truncated = pending.Count > 0 || matches.Count == 150 }));
        }
        catch (UnauthorizedAccessException)
        { return Task.FromResult(ToolReceipt.Fail("ACCESS_DENIED", "Search hit a protected directory.")); }
    }
}

public sealed class FileHashTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.sha256",
        "Compute the SHA-256 of a file without loading it entirely into memory.",
        ToolRisk.ReadLocal, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1}},
         "required":["path"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args,"path"));
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        if (!File.Exists(path)) return ToolReceipt.Fail("FILE_NOT_FOUND", "Missing file", new { path });
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return ToolReceipt.Ok("File hash", new { path, sha256 = Convert.ToHexString(hash).ToLowerInvariant() });
    }
}

/// <summary>
/// Create a single directory, never replacing an existing entry. Nested projects can
/// be built incrementally by calling this tool for each missing parent.
/// </summary>
public sealed class DirectoryCreateTool(WorkspacePathResolver paths) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("filesystem.mkdir",
        "Create one new directory. The parent must already exist; existing files and directories are preserved.",
        ToolRisk.WriteWorkspace, ToolSchemas.New("""
        {"type":"object","properties":{"path":{"type":"string","minLength":1,"maxLength":4096}},
         "required":["path"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args, "path"));

    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string target = GetTarget(args);
        if (Directory.Exists(target) || File.Exists(target))
            return Task.FromResult(ToolReceipt.Fail("ALREADY_EXISTS", "The destination already exists.", new { path = target }));
        string? parent = Path.GetDirectoryName(target);
        if (parent is null || !Directory.Exists(parent))
            return Task.FromResult(ToolReceipt.Fail("PARENT_NOT_FOUND", "Create the parent directory first.", new { path = target }));

        // Re-check path traversal just before the mutation; a reparse point must
        // not silently redirect a model-chosen path into another directory.
        _ = paths.Resolve(target);
        Directory.CreateDirectory(target);
        if (!Directory.Exists(target))
            throw new IOException("Directory creation returned without an observable directory.");
        return Task.FromResult(ToolReceipt.Ok("Created and verified directory", new { path = target }, changedState: true));
    }
}

/// <summary>
/// Optimistic, exact-span file editing. The model must first inspect and hash the
/// current file. Never silently apply an edit to a different revision or guess
/// between multiple identical matches. A same-directory staged replacement avoids
/// partially written output on crashes and checks the published bytes afterward.
/// </summary>
public sealed class FileEditTextTool(WorkspacePathResolver paths) : IAgentTool
{
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ToolDescriptor Descriptor { get; } = new("filesystem.edit_text",
        "Edit an existing UTF-8 file by replacing exactly one matching text span. First read and hash the file; pass its sha256 as expectedSha256. Refuses stale, ambiguous and no-op edits.",
        ToolRisk.WriteWorkspace, ToolSchemas.New("""
        {"type":"object","properties":{
          "path":{"type":"string","minLength":1,"maxLength":4096},
          "expectedSha256":{"type":"string","minLength":64,"maxLength":64},
          "oldText":{"type":"string","minLength":1,"maxLength":200000},
          "newText":{"type":"string","maxLength":200000}},
         "required":["path","expectedSha256","oldText","newText"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => paths.Resolve(ToolSchemas.String(args, "path"));

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string path = GetTarget(args);
        string expected = ToolSchemas.String(args, "expectedSha256");
        string oldText = ToolSchemas.String(args, "oldText");
        string newText = ToolSchemas.String(args, "newText");
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            return ToolReceipt.Fail("INVALID_HASH", "expectedSha256 must be a 64-character hexadecimal digest.");
        if (oldText == newText)
            return ToolReceipt.Fail("NO_CHANGE", "The replacement is identical to the existing text.");
        if (!File.Exists(path))
            return ToolReceipt.Fail("FILE_NOT_FOUND", "The file to edit does not exist.", new { path });
        var info = new FileInfo(path);
        if (info.Length > MaximumFileBytes)
            return ToolReceipt.Fail("FILE_TOO_LARGE", "File exceeds the supported 4 MiB editing limit.", new { path, bytes = info.Length });

        byte[] original = await File.ReadAllBytesAsync(path, ct);
        if (original.Length > MaximumFileBytes)
            return ToolReceipt.Fail("FILE_TOO_LARGE", "File grew beyond the editing limit.", new { path });
        string previousSha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        if (!previousSha.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return ToolReceipt.Fail("STALE_REVISION", "File changed since it was inspected. Re-read and re-hash before editing.",
                new { path, actualSha256 = previousSha });

        bool hasBom = original.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
        string text;
        try { text = StrictUtf8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0)); }
        catch (DecoderFallbackException)
        { return ToolReceipt.Fail("NOT_UTF8", "Editing requires valid UTF-8 source text.", new { path }); }
        int first = text.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0)
            return ToolReceipt.Fail("TEXT_NOT_FOUND", "The exact original text was not found; inspect the latest file.", new { path });
        if (text.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal) >= 0)
            return ToolReceipt.Fail("AMBIGUOUS_MATCH", "More than one matching span exists; supply a longer unique oldText.", new { path });

        string updated = string.Concat(text.AsSpan(0, first), newText, text.AsSpan(first + oldText.Length));
        byte[] payload = StrictUtf8.GetBytes(updated);
        if (hasBom) payload = [0xEF, 0xBB, 0xBF, .. payload];
        if (payload.Length > MaximumFileBytes)
            return ToolReceipt.Fail("RESULT_TOO_LARGE", "Edited file would exceed 4 MiB.", new { path });
        string newSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        string parent = Path.GetDirectoryName(path)!;
        string staged = Path.Combine(parent, ".nira-edit-" + Guid.NewGuid().ToString("N") + ".tmp");
        string rollback = Path.Combine(parent, ".nira-edit-" + Guid.NewGuid().ToString("N") + ".bak");
        bool published = false;
        bool verified = false;
        try
        {
            await File.WriteAllBytesAsync(staged, payload, ct);
            // Best-effort revalidation before atomic publication. Another writer
            // concurrently changing the file is still an OS-level race, so NIRA
            // never claims to provide a cross-process filesystem transaction.
            _ = paths.Resolve(path);
            string latest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))).ToLowerInvariant();
            if (!latest.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return ToolReceipt.Fail("STALE_REVISION", "The file changed while preparing the edit. No replacement was published.",
                    new { path, actualSha256 = latest });
            ct.ThrowIfCancellationRequested();
            File.Replace(staged, path, rollback, ignoreMetadataErrors: false);
            published = true;
            byte[] onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None);
            string observed = Convert.ToHexString(SHA256.HashData(onDisk)).ToLowerInvariant();
            if (!observed.Equals(newSha, StringComparison.Ordinal))
                throw new IOException("Published file did not match the staged content; reconciliation required.");
            verified = true;
            return ToolReceipt.Ok("Edited existing file and verified its published SHA-256", new {
                path, previousSha256 = previousSha, sha256 = observed, bytes = onDisk.Length,
                changes = 1 }, changedState: true);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            // Keep the rollback copy if verification fails after publication:
            // NIRA's durable dispatch journal will require reconciliation.
            if ((!published || verified) && File.Exists(rollback)) File.Delete(rollback);
        }
    }
}

/// <summary>Single fixed-origin search integration. No arbitrary HTTP request or SSRF path.</summary>
public sealed class TavilySearchTool(HttpClient http, Func<string?>? credential = null) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("web.search",
        "Search current public web results. Use a focused query, optional depth basic/advanced, maxResults 1–10 and includeDomains (up to five public hostnames, no URL paths). Requires NIRA_TAVILY_API_KEY. Returns candidate sources and excerpts, not inspected pages.",
        ToolRisk.ReadExternal, ToolSchemas.New("""
        {"type":"object","properties":{"query":{"type":"string","minLength":1,"maxLength":500},"depth":{"type":"string","enum":["basic","advanced"]},"maxResults":{"type":"integer"},"includeDomains":{"type":"array","items":{"type":"string","minLength":1,"maxLength":253}}},
         "required":["query"],"additionalProperties":false}
        """));
    private static (string Query, string Depth, int Count, string[] Domains) Options(JsonElement args)
    {
        string query = ToolSchemas.String(args, "query").Trim();
        string depth = args.TryGetProperty("depth", out var d) ? d.GetString()! : "basic";
        int count = 5;
        if (args.TryGetProperty("maxResults", out var c) && !c.TryGetInt32(out count))
            throw new ArgumentException("maxResults must be an integer from 1 to 10.");
        if (query.Length is < 1 or > 500 || query.Any(char.IsControl) || depth is not ("basic" or "advanced") || count is < 1 or > 10)
            throw new ArgumentException("Use a focused public query, basic/advanced depth and 1–10 results.");
        string[] domains = args.TryGetProperty("includeDomains", out var list)
            ? list.EnumerateArray().Select(d => d.GetString()!.Trim().ToLowerInvariant()).ToArray() : [];
        if (domains.Length > 5 || domains.Any(d => d.Length > 253 || !d.Contains('.') ||
            Uri.CheckHostName(d) != UriHostNameType.Dns || d.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))))
            throw new ArgumentException("Use up to five public DNS hostnames, not URLs or IP addresses.");
        foreach (string domain in domains) _ = PublicBrowserUrlPolicy.Parse("https://" + domain + "/");
        return (query, depth, count, domains.Distinct(StringComparer.Ordinal).ToArray());
    }
    public string GetTarget(JsonElement args)
    {
        _ = Options(args);
        return "https://api.tavily.com/search";
    }
    private static string ReadText(JsonElement value, string property, int maximum, string key)
    {
        if (!value.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String) return "";
        string text = (field.GetString() ?? "").Replace(key, "[redacted]", StringComparison.Ordinal).Trim();
        return text[..Math.Min(maximum, text.Length)];
    }
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!JsonToolSchema.Validate(Descriptor.Parameters, args, out string error))
            return ToolReceipt.Fail("ARGUMENT_SCHEMA", error);
        string query, depth; int limit; string[] domains;
        try { (query, depth, limit, domains) = Options(args); }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        { return ToolReceipt.Fail("INVALID_TARGET", "Search arguments are outside the supported bounds."); }
        string? key = credential is null ? Environment.GetEnvironmentVariable("NIRA_TAVILY_API_KEY") : credential();
        if (string.IsNullOrWhiteSpace(key))
            return ToolReceipt.Fail("SEARCH_KEY_MISSING", "Public search requires a Tavily API key. Configure NIRA_TAVILY_API_KEY.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
            var body = new Dictionary<string, object> { ["api_key"] = key, ["query"] = query, ["search_depth"] = depth,
                ["max_results"] = limit, ["include_answer"] = false };
            if (domains.Length > 0) body["include_domains"] = domains;
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                return ToolReceipt.Fail("WEB_SEARCH_FAILED", "Search provider returned HTTP " + (int)response.StatusCode);
            // Bound bytes, not characters. Never parse a cut-off JSON document as a result.
            const int maximumBytes = 512 * 1024;
            if (response.Content.Headers.ContentLength > maximumBytes)
                return ToolReceipt.Fail("SEARCH_RESPONSE_TOO_LARGE", "Search response exceeded the local size limit.");
            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk.AsMemory(), deadline.Token)) != 0)
            {
                if (buffer.Length + count > maximumBytes)
                    return ToolReceipt.Fail("SEARCH_RESPONSE_TOO_LARGE", "Search response exceeded the local size limit.");
                buffer.Write(chunk, 0, count);
            }
            using var parsed = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
                return ToolReceipt.Fail("SEARCH_RESPONSE_INVALID", "The provider did not return usable results.");
            var results = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray().Take(100))
            {
                if (results.Count >= limit) break;
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("url", out var field) || field.ValueKind != JsonValueKind.String) continue;
                string url = field.GetString() ?? "";
                if (url.Length is < 12 or > 2048 || url != url.Trim() || url.Any(char.IsControl) || url.Contains(key, StringComparison.Ordinal)) continue;
                Uri uri;
                try { uri = PublicBrowserUrlPolicy.Parse(url.Split('#')[0]); }
                catch (ArgumentException) { continue; }
                if (domains.Length > 0 && !domains.Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))) continue;
                if (!seen.Add(uri.AbsoluteUri)) continue;
                string title = ReadText(item, "title", 240, key);
                string excerpt = ReadText(item, "content", 1200, key);
                results.Add(new { title = title.Length == 0 ? uri.Host : title, url, excerpt });
            }
            if (results.Count == 0)
                return ToolReceipt.Fail("SEARCH_NO_USABLE_RESULTS", "No usable public HTTPS sources were returned.");
            return ToolReceipt.Ok("Live search results (candidate sources, not inspected pages)", new
            {
                observedUtc = DateTimeOffset.UtcNow, depth, requestedResults = limit, includeDomains = domains, results
            });
        }
        catch (JsonException) { return ToolReceipt.Fail("SEARCH_RESPONSE_INVALID", "Provider returned incomplete or invalid JSON."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return ToolReceipt.Fail("SEARCH_TIMEOUT", "The public search timed out."); }
        catch (Exception e) when (e is HttpRequestException or IOException)
        { return ToolReceipt.Fail("WEB_SEARCH_FAILED", "The public search could not be completed."); }
    }
}
