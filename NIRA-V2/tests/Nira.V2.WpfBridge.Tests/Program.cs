using NIRAAgent.AI.Cognition;
using NIRAAgent.Mind;

int passed = 0;
void Check(string title, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL " + title);
    Console.WriteLine("PASS " + title);
    passed++;
}

var e = NIRAMindEvent.UserMessage("/v2 Hello there.", Guid.NewGuid(), "user:conversation");
string enabled = NIRAV2CanaryRoutePolicy.EnableValue;
bool Route(NIRAMindEvent ev, string? flag = null) =>
    NIRAV2CanaryRoutePolicy.TryGetRequest(ev, flag ?? enabled, out _);

Check("explicit opt-in routes real user turns", Route(e));
Check("canary strips routing prefix before model input",
    NIRAV2CanaryRoutePolicy.TryGetRequest(e, enabled, out string prompt) && prompt == "Hello there.");
Check("without startup consent legacy remains default", !Route(e, "disabled"));
Check("case-mismatched startup consent cannot enable canary", !Route(e, "explicit_readonly"));
Check("ordinary messages remain with existing NIRA", !Route(e with { Content = "Hello there." }));
Check("ELVARA user messages are never canary-routed", !Route(e with { Name = "ExternalAppUserMessage" }));
Check("autonomous and internal events are never canary-routed",
    !Route(e with { Source = NIRAMindEventSource.Proactive }) &&
    !Route(e with { Source = NIRAMindEventSource.Internal }));
Check("attached images remain with legacy multimodal NIRA",
    !Route(e with { Metadata = new Dictionary<string, string> { [NIRAImageInput.MetadataKey] = "private" } }));
Check("attached past chats remain with their authoritative path",
    !Route(e with { Metadata = new Dictionary<string, string> { ["attachedPastChatSessionId"] = Guid.NewGuid().ToString("D") } }));
Check("selected local file paths remain with existing authorization",
    !Route(e with { Content = "/v2 Inspect files\n[Selected file paths; contents not observed]\nC:\\private.txt" }));
Check("empty and excessively long requests are not canary-routed",
    !Route(e with { Content = "/v2   " }) &&
    !Route(e with { Content = "/v2 " + new string('x', 5001) }));
Check("foreign control characters are rejected without reading files",
    !Route(e with { Content = "/v2 hi\u0001 there" }));
// Phase 4D: one WPF entry, unprefixed general model routing, strict host rollback.
// The original Phase 4A canary tests above must remain valid and executable.
string defaultMode = NIRAGeneralRuntimeRoutePolicy.DefaultReadOnlyValue;
bool DefaultRoute(NIRAMindEvent ev, string? mode = null, string? canary = null) =>
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(ev, canary, mode ?? defaultMode, out _);
var ordinary = e with { Content = "Hello NIRA, how are you today?" };
Check("unified entry selects ordinary conversation without /v2", DefaultRoute(ordinary));
Check("unified entry passes actual user text unchanged",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(ordinary, null, defaultMode, out string normalText) &&
    normalText == ordinary.Content);
Check("unified entry trims normal whitespace",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(ordinary with { Content = "  Hello NIRA.  " },
        null, defaultMode, out string trimmedText) && trimmedText == "Hello NIRA.");
Check("unified entry remains compatible with /v2 during migration",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(e, null, defaultMode, out string oldPrefixText) &&
    oldPrefixText == "Hello there.");
Check("no default mode keeps original legacy behavior",
    !NIRAGeneralRuntimeRoutePolicy.TryGetRequest(ordinary, null, null, out _));
Check("original explicit canary still works with no unified mode",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(e, enabled, null, out string canaryText) &&
    canaryText == "Hello there.");
Check("legacy kill switch blocks default runtime even with canary enabled",
    !DefaultRoute(ordinary, NIRAGeneralRuntimeRoutePolicy.LegacyOnlyValue, enabled) &&
    !DefaultRoute(e, NIRAGeneralRuntimeRoutePolicy.LegacyOnlyValue, enabled));
Check("invalid or lower-case modes cannot enable the new runtime",
    !DefaultRoute(ordinary, "v2_default_readonly") &&
    !DefaultRoute(ordinary, "V2_DEFAULT_READONLY ") &&
    !DefaultRoute(ordinary, "UNRECOGNIZED", enabled));
Check("unified entry never routes ELVARA messages",
    !DefaultRoute(ordinary with { Name = "ExternalAppUserMessage" }));
Check("unified entry never routes internal or autonomous events",
    !DefaultRoute(ordinary with { Source = NIRAMindEventSource.Internal }) &&
    !DefaultRoute(ordinary with { Source = NIRAMindEventSource.Proactive }));
Check("unified entry leaves attached images to existing multimodal runtime",
    !DefaultRoute(ordinary with { Metadata = new Dictionary<string, string> {
        [NIRAImageInput.MetadataKey] = "local-attachment" } }));
Check("unified entry leaves attached past chats on authoritative scope",
    !DefaultRoute(ordinary with { Metadata = new Dictionary<string, string> {
        ["attachedPastChatSessionId"] = Guid.NewGuid().ToString("D") } }));
Check("unified entry does not bypass selected local-file context authorization",
    !DefaultRoute(ordinary with { Content = "Inspect this. [Selected file paths; contents not observed]" }));
Check("unified entry rejects unsolicited embedded real-image context",
    !DefaultRoute(ordinary with { Content = "Please inspect. [Real image data accompanies this turn." }));
Check("unified entry rejects whitespace-only request",
    !DefaultRoute(ordinary with { Content = "   " }));
Check("unified entry rejects oversized user content",
    !DefaultRoute(ordinary with { Content = new string('a', 5001) }));
Check("unified entry rejects foreign control characters",
    !DefaultRoute(ordinary with { Content = "hello\u0001world" }));
Check("unified entry preserves current-user path text without selecting a tool",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(
        ordinary with { Content = "Check Documents/NIRA_R9_TOOL_LAB_01 exists" },
        null, defaultMode, out string pathRequest) &&
    pathRequest == "Check Documents/NIRA_R9_TOOL_LAB_01 exists");
Check("unified entry doesn't keyword-route: general model decides whether tools are needed",
    NIRAGeneralRuntimeRoutePolicy.TryGetRequest(
        ordinary with { Content = "Please calculate 17 * 13 and research NASA" },
        null, defaultMode, out string combinedTask) &&
    combinedTask == "Please calculate 17 * 13 and research NASA");
Check("unified entry does not turn model-written text into autonomous authority",
    !DefaultRoute(ordinary with { Source = NIRAMindEventSource.Perception }));

Console.WriteLine("RESULT: " + passed + " Phase4D combined routing checks passed; 0 failed.");
return 0;
