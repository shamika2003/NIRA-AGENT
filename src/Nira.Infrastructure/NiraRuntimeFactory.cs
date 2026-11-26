using Nira.Core;

namespace Nira.Infrastructure;

public sealed class NiraRuntime : IDisposable
{
    private readonly HttpClient _http;
    private readonly EdgePublicBrowserSessions _browserSessions;
    public NiraEngine Engine { get; }
    public IConversationStore Conversations { get; }
    public string Workspace { get; }
    public string DatabasePath { get; }
    public bool DevelopmentPermissions { get; }
    public NiraModelCredentialStore ModelCredentials { get; }

    private NiraRuntime(NiraEngine engine, IConversationStore conversations,
        HttpClient http, string workspace, string database, NiraModelCredentialStore modelCredentials,
        EdgePublicBrowserSessions browserSessions, bool developmentPermissions)
    { DevelopmentPermissions = developmentPermissions; Engine = engine; Conversations = conversations; _http = http; _browserSessions = browserSessions;
      Workspace = workspace; DatabasePath = database; ModelCredentials = modelCredentials; }

    public static NiraRuntime CreateDefault()
    {
        string appRoot = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "ELVARA", "NIRA-Clean");
        // Windows Controlled Folder Access and redirected Documents folders can block
        // directory creation before the agent starts. Keep NIRA's own default workspace
        // under LocalAppData, beside its existing durable database, rather than assuming
        // that Documents is accessible. An explicit NIRA_WORKSPACE override is honored.
        try { Directory.CreateDirectory(appRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new IOException($"NIRA's application data directory is unavailable: '{appRoot}'. " +
                "Check Windows Security's Protection history and filesystem permissions.", ex);
        }
        string workspace = NiraWorkspaceLocation.Open(appRoot,
            Environment.GetEnvironmentVariable("NIRA_WORKSPACE"));
        string database = Path.Combine(appRoot, "nira.db");
        var state = new SqliteConversationStore(database);
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        Uri endpoint = new(Environment.GetEnvironmentVariable("NIRA_MODEL_ENDPOINT") ?? "https://ollama.com/v1/");
        string model = Environment.GetEnvironmentVariable("NIRA_MODEL_NAME") ?? "gpt-oss:120b";
        var modelCredentials = new NiraModelCredentialStore(appRoot, endpoint);
        var client = new OpenAiCompatibleModel(http, endpoint, model, modelCredentials.Resolve);
        var sessions = new EdgePublicBrowserSessions();
        // Vision is opt-in and separate from the agent's reasoning model.
        // Only reuse the primary credential when the exact API base matches.
        string? visionEndpoint = Environment.GetEnvironmentVariable("NIRA_VISION_ENDPOINT");
        string? visionName = Environment.GetEnvironmentVariable("NIRA_VISION_MODEL");
        VisionProvider? vision = null;
        if (!string.IsNullOrWhiteSpace(visionEndpoint) || !string.IsNullOrWhiteSpace(visionName))
        {
            if (string.IsNullOrWhiteSpace(visionEndpoint) || string.IsNullOrWhiteSpace(visionName))
                throw new ArgumentException("Set both NIRA_VISION_ENDPOINT and NIRA_VISION_MODEL, or neither.");
            if (!Uri.TryCreate(visionEndpoint, UriKind.Absolute, out Uri? visionBase))
                throw new ArgumentException("NIRA_VISION_ENDPOINT must be an absolute API base URL.");
            vision = new VisionProvider(visionBase, visionName, () =>
                Environment.GetEnvironmentVariable("NIRA_VISION_API_KEY") ??
                (string.Equals(visionBase.AbsoluteUri, endpoint.AbsoluteUri, StringComparison.Ordinal) ? modelCredentials.Resolve() : null));
        }
        ProceduralSkillService? procedures = null;
        var skillRecall = new SkillRecallTool((id, ct) => procedures!.RetrieveAsync(id, ct));
        var tools = NiraStandardTools.Create(workspace, state, http, sessions, vision: vision,
            imageGenerationResolver: () => ImageGenerationProvider.Load(appRoot), proceduralRecall: skillRecall);
        procedures = new ProceduralSkillService(state, client, tools);
        // Default to bounded routing in both CLI and desktop; retain a diagnostic opt-out.
        bool routing = Environment.GetEnvironmentVariable("NIRA_FULL_TOOL_CATALOG") != "1";
        bool adaptive = Environment.GetEnvironmentVariable("NIRA_LEGACY_PREFLIGHT") != "1";
        // Debug builds use the owner-requested low-friction policy. Release builds
        // cannot enable it via an environment variable or model/tool content.
        bool developmentPermissions = false;
#if DEBUG
        developmentPermissions = Environment.GetEnvironmentVariable("NIRA_DEVELOPMENT_PERMISSIONS") != "0";
#endif
        var engine = new NiraEngine(client, tools, state, new WorkspaceAuthority(workspace, developmentPermissions),
            enableCapabilityRouting: routing, enableAdaptivePlanning: adaptive, memoryContext: new SemanticMemoryContextProvider(state, client),
            memoryLearning: new MemoryLearningService(state, client), proceduralSkills: procedures, enableLocalSchemaRanking: true);
        return new NiraRuntime(engine, state, http, workspace, database, modelCredentials, sessions, developmentPermissions);
    }

    public void Dispose()
    {
        try { _browserSessions.Dispose(); }
        finally { _http.Dispose(); }
    }
}

/// <summary>
/// Opens the actual user-writable filesystem workspace used by all NIRA tools.
/// Location policy is independent of the model: the model must never acquire
/// filesystem privileges simply by choosing a different path in its response.
/// </summary>
public static class NiraWorkspaceLocation
{
    public static string Open(string applicationDataRoot, string? configuredWorkspace)
    {
        string appRoot = Path.GetFullPath(applicationDataRoot);
        bool explicitlyConfigured = !string.IsNullOrWhiteSpace(configuredWorkspace);
        string workspace;
        if (explicitlyConfigured)
        {
            string requested = Environment.ExpandEnvironmentVariables(
                configuredWorkspace!.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(requested))
                throw new ArgumentException(
                    "NIRA_WORKSPACE must be an absolute folder path. " +
                    "Choose an existing writable folder, or unset it to use NIRA's default.",
                    nameof(configuredWorkspace));
            workspace = Path.GetFullPath(requested);
        }
        else
        {
            workspace = Path.Combine(appRoot, "Workspace");
        }

        // Test actual create/write/delete rights. Merely seeing that a directory
        // exists is not evidence that Windows will allow NIRA to write into it.
        try
        {
            Directory.CreateDirectory(workspace);
            for (string? component = workspace; component is not null;
                component = Path.GetDirectoryName(component))
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(
                        "Workspace path contains a symbolic link or junction; choose a real directory.");
            }
            string probe = Path.Combine(workspace,
                ".nira-access-" + Guid.NewGuid().ToString("N") + ".tmp");
            using var stream = new FileStream(probe, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
            stream.WriteByte(0x4e);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            string suggestion = explicitlyConfigured
                ? "The selected NIRA_WORKSPACE is blocked or unavailable. " +
                  "Choose another authorized writable folder, or remove NIRA_WORKSPACE to use the default."
                : "Windows blocked NIRA's app-private workspace. " +
                  "Check Windows Security > Protection history and folder permissions; " +
                  "alternatively set NIRA_WORKSPACE to an approved writable folder.";
            throw new IOException(
                $"NIRA could not initialize its workspace '{workspace}'. {suggestion} " +
                $"Underlying Windows error: {ex.GetType().Name}: {ex.Message}", ex);
        }
        return workspace;
    }
}
