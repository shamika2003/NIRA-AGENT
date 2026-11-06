# NIRA V2 Phase 4A — in-process WPF bridge (explicit read-only canary)

## What this integrates

- Existing `NIRAAgent.UI` + `MainWindowViewModel` + `NIRAMindRuntime` continue to own the visible NIRA window, voice queue, orb, input/output, and the original conversation archive.
- An optional `INIRAGeneralRuntimeBridge` implementation runs `AgentLoopEngine` **in the same UI process**, using existing `GeneralOllamaReasoner`, registered observable tools and Windows DPAPI-encrypted V2 checkpoints.
- The V2 route is selected only when the host startup switch equals `EXPLICIT_READONLY`, AND the user's normal desktop message starts with `/v2 `, AND no unsupported attachments/past-chat scope are present. This is a temporary migration switch, **not semantic task routing**: once selected the model chooses tools through `CapabilityRegistry`.
- ELVARA embedded messages, proactive/background work, and all normal NIRA messages stay on the existing NIRAExecutive.
- The existing conversation manager records the one human message and one successful final V2 assistant reply. No copied memory store is created. No V2-generated reply is recorded as successful if the agent run is incomplete.
- The old desktop actions, character, emotional evolution, long-term memory formation, and all other existing capabilities remain legacy-owned. V2 does **not** access them yet; this is not full parity.

## Safety model

- Canary off by default; no V2 cloud/PC work until selected for a specific turn.
- All registered V2 tools are read-only: math, platform, public web search when configured, and optional consented process-name observation.
- No file reads, desktop writes, browser control, shell, application automation, screenshots, window titles or private-memory retrieval through V2.
- Every V2 tool call remains under the existing `AgentLoopEngine`, its capability schema, DPAPI vault and observe-only permission gate.
- Separate PC name consent `NIRA_V2_PC_OBSERVATION=ALLOW_APP_NAMES` must also be set to advertise Windows observation; names can leave the PC and reach the cloud.
- Never send private data during this canary. The remote model receives `/v2` message content (without prefix) and authorized tool observations. NIRA's existing conversation archive is governed by the **legacy** local-storage policy.
- UI progress codes are host-authored; provider response bodies, credentials and raw exception messages are not shown.
- A failed canary is reported honestly; it never silently executes the request a second time on legacy. To use legacy, resend **without** the `/v2` prefix.

## Start and stop (Windows PowerShell)

From `NIRA-AGENT` root, **exit any running NIRA windows first**:

```powershell
$env:NIRA_V2_WPF_CANARY = "EXPLICIT_READONLY"
$env:NIRA_V2_MODEL = "gpt-oss:120b"
# Optional separate PC-name consent (disabled by default):
# $env:NIRA_V2_PC_OBSERVATION = "ALLOW_APP_NAMES"
dotnet run --project ".\NIRAAgent.UI\NIRAAgent.UI.csproj" -c Debug
```

Test an ordinary legacy message `Hi NIRA` and an explicit canary message `/v2 Calculate 125 * 24 and explain.`; to test permitted app names, use `/v2 Which applications have visible windows on my Windows PC?` with the separate observation flag enabled.

To disable, exit the app and run without the `NIRA_V2_WPF_CANARY` environment variable, or remove the flag in the same PowerShell:

```powershell
Remove-Item Env:NIRA_V2_WPF_CANARY -ErrorAction SilentlyContinue
Remove-Item Env:NIRA_V2_PC_OBSERVATION -ErrorAction SilentlyContinue
```

## Release gates

1. Audit source hashes; install to exact source baseline with backups and rollback on failures.
2. All 146 V2 checks remain green + 12 bridge routing tests.
3. Windows WPF build succeeds; existing `NIRAAgent.RegressionChecks` build succeeds.
4. Real WPF manual parity: legacy text message, opt-in canary text, in-process tool observation, concurrent user turns, cancellation/closing, voice and orb unaffected, no duplicate archive entry.
5. Only after parity/identity/context and authorization verification, move from the explicit canary toward a production runtime switch and original capability migration.
