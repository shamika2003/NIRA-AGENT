# Phase 1A — Real model adapter, NO tools

This is a narrow production-minded integration milestone. An Ollama-compatible model
is connected to the Phase 0 persistent agent runtime using `/api/chat`, `stream:false`,
and a structured JSON decision. The provider is replaceable. **The live pilot is not
NIRA's finished intelligence, does not load legacy identity/memory, and cannot browse
or research current facts.** It deliberately exposes no tools until there is a typed,
authorized argument/evidence vault.

## Why this release is small

The broken R4 runtime mixed reasoning, retrieval, media, verification and rendering.
Phase 1A proves one seam before loading more dependencies. There are no R-series
fallbacks, no source-excerpt recovery, and no new copy of the legacy Executive.

## Run after the offline tests

From `NIRA-V2` in PowerShell:

```powershell
$env:NIRA_V2_MODEL = 'gpt-oss:120b-cloud'
$env:NIRA_V2_OLLAMA_URL = 'http://127.0.0.1:11434/'
dotnet run --project .\apps\Nira.V2.LivePilot -- 'Explain what a durable workflow is in simple language.'
```

Your Ollama service must be running and authenticated for cloud models. The prompt
is sent to the configured model provider; cloud models may transmit it off-device.
**Do not test with passwords, customer records, private documents or secrets.**
The existing Phase 0 local JSON store is unencrypted; pilot checkpoints are in
`%TEMP%\NIRA-V2-PilotRuns` and may be deleted after testing.

## Acceptance gates

1. Both original V2 offline suites still pass, unchanged.
2. The eight provider contract tests pass against an HTTP stub without network.
3. LivePilot builds and a real chosen model produces a sensible response.
4. Pilot progress shows persisted `RUN_CREATED`, `RUN_ACTIVE`, `REASONING_STARTED`, `RUN_COMPLETED`.
5. A provider/network outage returns `Incomplete` rather than a fake answer.
6. `NIRAAgent/` and `NIRAAgent.UI/` are untouched.

Next: model registry/routing + typed tool argument vault + permissioned observe-only
research adapters. Only after those pass can NIRA complete evidence-dependent tasks.
