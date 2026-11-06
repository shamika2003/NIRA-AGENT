# NIRA V2 — Phase 3A: General Agent Core (first vertical slice)

## Product scope (non-negotiable)

NIRA is **one** personal general-purpose agent. Normal conversation, files,
PC control, browser, research, voice, memory, images, documents, and complex
cross-domain workflows must ultimately share one adaptive reasoning loop.
This package does not create a second NIRA identity or a research sub-agent.

The prior `OllamaResearchReasoner` accepted *only* `web.search`. The new
`GeneralOllamaReasoner` accepts a **host-defined capability registry**, builds
provider-native tool schemas at runtime from those descriptors, and validates
all model-proposed calls against the registered tool's validator. There is no
hardcoded conversation-vs-search router or fixed chain of tool calls.

## Actual code shipped

- `Nira.V2.General/CapabilityRegistry`: host-controlled tool identity,
  provider function name, JSON argument schema, access level and validation.
  Third-party capabilities implement `IGeneralCapability`. Duplicate IDs,
  duplicate model function names and unexpected tool registries are rejected.
- `GeneralOllamaReasoner`: uses direct Ollama Cloud through the existing
  credential resolver, native function calls and ordinary conversational
  responses. Each model-selected tool is given a **host-minted, run-scoped**
  argument token and executed by the existing checkpointing `AgentLoopEngine`.
- `GeneralObservationContext`: bounded presentation of real observations;
  public web citations `[S#]` bind to host-observed URLs, not fabricated links.
- Real observe-only tools: `math.calculate` (deterministic decimal arithmetic),
  `system.platform` (non-identifying OS family/architecture/runtime), and
  `web.search` (existing Tavily handler, only if its key is available).
- `Nira.V2.GeneralPilot`: live CLI demonstrating normal conversation, tool
  selection, multi-tool continuation, failure observations and citations.
- `Nira.V2.General.Tests`: 18 offline test cases using fake HTTP transport,
  including dynamic schemas, normal no-tool answers, tool chains, citation
  source IDs, cross-tool scope rejection and unsafe function/JSON rejection.

## Scope and privacy limitations

This is a REAL multi-capability runtime *vertical slice*, NOT yet a production
NIRA application. It deliberately has NO arbitrary filesystem, process,
terminal, application, desktop, screenshot, browser-automation or other
state-changing capability. The legacy NIRA executable and all persistent
personality, character, memory, privacy and ELVARA state are not modified.

**Do not send private data, passwords or user documents to GeneralPilot.** The
existing `AgentPayloadVault` and `FileAgentStateStore` store public test
requests and tool data in **unencrypted** local JSON under Temp. Ollama Cloud
processes model requests remotely. The pilot prints activity/status, but has
no built-in encrypted log retention or data deletion policy.

No model text can register a tool, choose a vault token, bypass the read-only
permission gate or execute an unlisted Windows command. Neither tool receipt
nor model prose is proof of a full user task being completed; integration of
independently verified PC actions is a required future release.

## Run (Windows PowerShell, after guarded installation)

```powershell
Set-Location "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT\NIRA-V2"
$env:NIRA_V2_MODEL = "gpt-oss:120b"
dotnet run --project .\apps\Nira.V2.GeneralPilot -- "Hello NIRA. How are you?"
dotnet run --project .\apps\Nira.V2.GeneralPilot -- "Calculate (14.5 + 7.5) * 3 and tell me the exact answer."
dotnet run --project .\apps\Nira.V2.GeneralPilot -- "Research what infrared wavelengths the James Webb Space Telescope observes using NASA sources, and calculate 14 * 3. Cite the sources."
```

The last task needs a previously configured `NIRA_TAVILY_API_KEY` or
`TAVILY_API_KEY`. Without Tavily, web search is simply absent from the tool
catalog; ordinary conversation and arithmetic still function. All prompts
must be non-sensitive. No local Ollama app is required.

## Production readiness gates, before any private desktop execution

1. Replace unencrypted checkpoints and argument/result vaults with secure,
   versioned, crash-consistent, per-user encrypted persistence; prove restore.
2. Integrate one authoritative NIRA identity/memory/conversation context
   service (do not fork persona or duplicate memory stores).
3. Implement scoped authorization and independent postcondition receipts for
   desktop/filesystem/browser operations, starting with read-only observation.
   Never trust a model's assertion that an action succeeded.
4. Add dynamic tool discovery budgets, provider-agnostic model routing,
   context summarization and resource/cost controls.
5. Add real multi-turn chat session persistence and WPF progress/approval UI.
6. Add adversarial prompt-injection, cross-capability authorization, kill/restart,
   idempotency, denial-of-service and unseen cross-domain task evaluations.
7. Retire old Executive/three-block code only after parity and data migration
   tests succeed, retaining a verified rollback plan.

**A passing 114/114 offline check target is a build gate, not a production
security certification.** It does not prove that the hosted model will always
choose the correct tool or that NIRA has full PC control yet.
