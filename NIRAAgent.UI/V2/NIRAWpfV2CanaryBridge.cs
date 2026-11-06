using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using NIRAAgent.Character.State;
using NIRAAgent.Capabilities;
using NIRAAgent.Conversation;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;
using Nira.V2.Windows;
using Nira.V2.Filesystem;
using Nira.V2.LegacyAdapters;

namespace NIRAAgent.UI.V2;

/// <summary>
/// In-process WPF/agent-loop adapter. No new character, memory database, window or
/// autonomous authority is created. Runtime-selection is host-gated; tool choice is
/// model-driven through registered read-only capabilities.
/// </summary>
public sealed class NIRAWpfV2CanaryBridge : INIRAGeneralRuntimeBridge
{
    private readonly NIRAV2ReadOnlyContextBuilder _contextBuilder;
    private readonly NIRACapabilityService _authoritativeLegacyCapabilities;

    public NIRAWpfV2CanaryBridge(ConversationManager conversation,
        NIRACharacterStateService character, NIRALongTermMemoryService memories,
        NIRAConversationArchiveStore archive, NIRACapabilityService legacyCapabilities)
    {
        _authoritativeLegacyCapabilities = legacyCapabilities ?? throw new ArgumentNullException(nameof(legacyCapabilities));
        _contextBuilder = new NIRAV2ReadOnlyContextBuilder(conversation, character, memories, archive);
    }

    public bool CanHandle(NIRAMindEvent mindEvent) =>
        NIRAGeneralRuntimeRoutePolicy.TryGetRequest(mindEvent,
            Environment.GetEnvironmentVariable("NIRA_V2_WPF_CANARY"),
            Environment.GetEnvironmentVariable("NIRA_AGENT_RUNTIME"), out _);

    public async IAsyncEnumerable<NIRAOutputChunk> RunAsync(NIRAMindEvent mindEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!NIRAGeneralRuntimeRoutePolicy.TryGetRequest(mindEvent,
            Environment.GetEnvironmentVariable("NIRA_V2_WPF_CANARY"),
            Environment.GetEnvironmentVariable("NIRA_AGENT_RUNTIME"), out string request))
            throw new InvalidOperationException("V2 runtime route is not authorized.");

        var channel = Channel.CreateUnbounded<NIRAOutputChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        Task producer = Task.Run(() => ProduceAsync(mindEvent.Content, request,
            channel.Writer, cancellationToken),
            cancellationToken);
        await foreach (NIRAOutputChunk chunk in channel.Reader.ReadAllAsync(cancellationToken))
            yield return chunk;
        await producer;
    }

    private async Task ProduceAsync(string submittedInput, string request,
        ChannelWriter<NIRAOutputChunk> output,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OllamaCloudCredentials.ReadExistingKey()))
            {
                WriteText(output, "NIRA V2 is unavailable: the cloud model key isn't configured for this Windows session.");
                return;
            }
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                WriteText(output, "NIRA V2 cannot access the protected local-state folder.");
                return;
            }
            // Isolated encrypted canary checkpoints; legacy conversation/history is not copied.
            string root = Path.Combine(baseFolder, "NIRA", "V2", "WpfCanarySecure");
            var protector = new WindowsDpapiDataProtector();
            var vault = new AgentPayloadVault(Path.Combine(root, "vault"), protector);
            using var modelHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var searchHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var modelHttp = new HttpClient(modelHandler) { Timeout = TimeSpan.FromMinutes(4) };
            using var searchHttp = new HttpClient(searchHandler) { Timeout = TimeSpan.FromSeconds(45) };
            var capabilities = new List<IGeneralCapability>
            {
                new CalculatorCapability(vault), new PlatformCapability(vault)
            };
            if (!string.IsNullOrWhiteSpace(TavilySearchTool.ReadKey()))
                capabilities.Add(new SearchCapability(new TavilySearchTool(searchHttp, vault)));
            // The independent PC consent flag is intentionally unchanged.
            capabilities.AddRange(WindowsObservationRegistration.Create(vault,
                Environment.GetEnvironmentVariable("NIRA_V2_PC_OBSERVATION")));
            // One-turn read-only filesystem grant. Registered ONLY if the owner
            // opted in AND this particular user turn names exactly one known-folder
            // relative path; old memories/tool output never grant a file scope.
            capabilities.AddRange(RequestedPathObservationRegistration.Create(vault,
                Environment.GetEnvironmentVariable("NIRA_V2_FILE_OBSERVATION"), request));
            // Phase 4E: first real legacy-service ownership adapter. V2 chooses whether
            // to read, but the existing NIRA authority, audit and handler execute it.
            // This is a separate opt-in, and is registered only for one exact user path.
            capabilities.AddRange(LegacyTextReadRegistration.Create(vault,
                new NIRALegacyTextReadExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_TEXT_READ"), request));
            // Phase 4E.2: the existing NIRA capability service owns and audits the
            // exact-directory listing; a model cannot supply a path, glob or recursion.
            capabilities.AddRange(LegacyDirectoryListRegistration.Create(vault,
                new NIRALegacyDirectoryListExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_DIRECTORY_LIST"), request));
            var registry = new CapabilityRegistry(capabilities);
            // Personal legacy context is read ONLY after a separate explicit host consent.
            // It is not written to any V2 memory store. Model requests still leave the PC.
            NIRAV2ReadOnlyContext continuity = await _contextBuilder.BuildAsync(
                submittedInput, request,
                Environment.GetEnvironmentVariable("NIRA_V2_PERSONAL_CONTEXT"), ct);
            string model = Environment.GetEnvironmentVariable("NIRA_V2_MODEL") ?? "gpt-oss:120b";
            var reasoner = new GeneralOllamaReasoner(modelHttp, OllamaReasonerOptions.Cloud(model),
                registry, vault, maximumTools: 5,
                trustedIdentity: continuity.TrustedIdentity,
                untrustedHistory: continuity.UntrustedHistory);
            var progress = new DirectProgress(entry =>
            {
                string? safeMessage = entry.Code switch
                {
                    "REASONING_STARTED" => "Thinking through the request...",
                    "TOOL_DISPATCHED" => "Using a permitted tool...",
                    "TOOL_SUCCEEDED" => "Reviewing the observed result...",
                    "RUN_COMPLETED" => "Finishing the answer...",
                    _ => null
                };
                if (safeMessage is not null)
                    output.TryWrite(new NIRAOutputChunk
                    {
                        Source = NIRAMindEventSource.User,
                        Type = NIRAOutputChunkType.Progress,
                        Content = safeMessage
                    });
            });
            var engine = new AgentLoopEngine(reasoner, new FileAgentStateStore(root, protector),
                new ObserveOnlyPermissionGate(), registry.Tools, new AgentLoopLimits(9, 5), progress);
            AgentRunSnapshot run = await engine.StartAsync(request, ct);
            if (run.Status == AgentRunStatus.Completed && !string.IsNullOrWhiteSpace(run.FinalAnswer))
            {
                output.TryWrite(new NIRAOutputChunk
                {
                    RunId = run.Id,
                    Source = NIRAMindEventSource.User,
                    Type = NIRAOutputChunkType.Text,
                    Content = run.FinalAnswer
                });
                output.TryWrite(new NIRAOutputChunk
                {
                    RunId = run.Id,
                    Source = NIRAMindEventSource.User,
                    Type = NIRAOutputChunkType.Completed
                });
            }
            else
            {
                string code = CleanCode(run.LastErrorCode ?? reasoner.LastFailureCode);
                WriteText(output, "The V2 agent couldn't complete this request (" + code + "). No desktop changes were made.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.TryWrite(new NIRAOutputChunk
            {
                Source = NIRAMindEventSource.User,
                Type = NIRAOutputChunkType.Cancelled
            });
        }
        catch (Exception)
        {
            // No exception bodies, filesystem paths, cloud response bodies, or credentials in UI.
            WriteText(output, "NIRA encountered a runtime or network error. No unapproved actions were performed.");
        }
        finally
        {
            output.TryComplete();
        }
    }

    private static void WriteText(ChannelWriter<NIRAOutputChunk> writer, string text) =>
        writer.TryWrite(new NIRAOutputChunk
        {
            Source = NIRAMindEventSource.User,
            Type = NIRAOutputChunkType.Text,
            Content = text
        });

    private static string CleanCode(string? code) =>
        code is { Length: > 0 and <= 60 } &&
        Regex.IsMatch(code, "^[A-Z0-9_]+$", RegexOptions.CultureInvariant)
            ? code : "INCOMPLETE";

    private sealed class DirectProgress(Action<AgentActivityEvent> report) : IProgress<AgentActivityEvent>
    {
        public void Report(AgentActivityEvent value) => report(value);
    }
}
