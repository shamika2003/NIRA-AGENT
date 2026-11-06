# NIRA Runtime V2 — Phase 1C: Read-only observed research loop

## What this phase proves

The Phase 1B live cloud pilot completed one model response. Phase 1C adds a separate,
restricted research pilot that can perform repeated `reason -> web.search -> observe -> reason`
cycles, using real Ollama Cloud and Tavily. The Phase 1B adapter is untouched.

**This is not production release approval.** It is an end-to-end vertical slice.

- The model may propose **only** `web.search`, with JSON `{ "query": "..." }`.
- The runtime strictly checks tool identity, schema and query size before creating
  a random opaque reference. The model does not choose a file path or vault token.
- A fixed URL Tavily adapter performs the search. It does not visit arbitrary
  websites, launch browsers, or execute page instructions.
- Search results are attributed public-source excerpts in a run-scoped payload vault.
- The next cloud decision receives those actual results as untrusted data.
- Citation links in the generated answer MUST match observed source URLs.
  This prevents fabricated URLs, **not** every possible factual misinterpretation.
- The existing AgentLoopEngine persists activity after each step, enforces budgets,
  refuses unknown tools, and supports read-only restart recovery.

## Security limitations

- This Phase 1C development vault and checkpoint store are **NOT ENCRYPTED**.
  Use only non-sensitive public research prompts. Do not send personal documents,
  passwords, banking details or private files through this pilot.
- `NIRA_OLLAMA_API_KEY` and `NIRA_TAVILY_API_KEY` are read from process/Windows User
  environment and placed ONLY in outbound HTTPS Authorization headers. Never log them.
- Cloud and Tavily services process prompts and public search queries remotely.
- Run the pilot with HTTP auto-redirects and cookies disabled (included in the app).
- No write, execution, desktop control, filesystem browsing, or image retrieval tools
  are registered. The old NIRAAgent and ELVARA bridge are untouched.
- No claim-level fact-checking or verified image pipeline is included here. Search
  results are source extracts, not verified truth. These are later milestones.
- Model decisions must be valid JSON. Malformed decisions terminate this pilot
  instead of executing an unrecognized instruction.

## How to run (after installer passes)

```powershell
Set-Location "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT\NIRA-V2"

$env:NIRA_V2_MODEL = "gpt-oss:120b"

# Check availability without ever printing the actual API keys:
[bool]([Environment]::GetEnvironmentVariable("NIRA_OLLAMA_API_KEY", "User"))
[bool]([Environment]::GetEnvironmentVariable("NIRA_TAVILY_API_KEY", "User"))

dotnet run --project ".\apps\Nira.V2.ResearchPilot" -- `
    "Research why the James Webb Space Telescope observes infrared light. Use NASA or ESA and cite the sources."
```

Expected milestones: RUN_CREATED, RUN_ACTIVE, REASONING_STARTED,
TOOL_PROPOSED web.search, TOOL_DISPATCHED, TOOL_SUCCEEDED, REASONING_STARTED,
RUN_COMPLETED, STATUS: Completed. The model may need more than one search.

If the Tavily key is not in User environment, but the original application uses a
local credential store, **do not copy the raw key into chat or a log**. A future
credential adapter can read from the existing protected store without duplicating
or exposing the secret.

## Remaining production requirements

1. Encrypt persisted task input, prompts and retrieved evidence at rest.
2. Introduce a general typed capability schema registry (not a one-tool pilot).
3. Build resilient search/extract and claim-level evidence verification.
4. Add authenticated, visually verified documentary media.
5. Wire existing NIRA character/memory/personality as separate services.
6. Expose durable, user-friendly progress events in the existing WPF interface.
7. Add provider failure classifications to runtime events, correlation IDs, telemetry
   and privacy-preserving diagnostics.
8. Prove real-provider performance, interrupted-run recovery, authorization,
   prompt injection containment and red-team scenarios before cutover.

## Files introduced (add-only)

- Runtime: `AgentPayloadVault.cs`
- Provider: `OllamaResearchReasoner.cs`
- Web adapter: `Nira.V2.Web.csproj`, `TavilySearchTool.cs`
- Pilot: `Nira.V2.ResearchPilot.csproj`, `Program.cs`
- Tests: `Nira.V2.ResearchLoop.Tests.csproj`, `Program.cs`
- Documentation: this file.

No Phase 0, Phase 1A, Phase 1B, legacy NIRA or ELVARA source is replaced.
