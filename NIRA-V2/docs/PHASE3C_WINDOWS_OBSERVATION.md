# NIRA V2 — Phase 3C: Win32 observation through the general capability registry

**Status:** Production-oriented, read-only Windows integration milestone. It is **not** desktop automation, an approved production deployment, or a substitute for NIRA's existing WPF/identity/memory systems.

## Actual architectural integration

- The existing `Nira.V2.General.GeneralOllamaReasoner` and `AgentLoopEngine` are unchanged. They enumerate registered capabilities dynamically and checkpoint actual calls and results.
- `Nira.V2.Windows` implements the same `IGeneralCapability` contract as calculator/platform/search. It is an isolated host adapter, not a second model, intent classifier, special keyword route, or scripted PC assistant.
- The existing `Nira.V2.GeneralPilot` registers two **read-only** Win32 capabilities only when `NIRA_V2_PC_OBSERVATION` has the exact temporary per-process value `ALLOW_APP_NAMES`. No change to existing system/user Windows environment variables is necessary.
- `windows.visible_apps` observes process **names** associated with visible top-level windows. `windows.foreground_app` observes only the foreground application's process name. It does not inspect titles, files, icons, text, folders, command lines, screenshots, keystrokes, clipboard, or browser pages.
- Native `EnumWindows` and `GetForegroundWindow` run in a bounded enumeration; each process name comes from `Process.GetProcessById`, sanitized to short ASCII identifiers. Native handles/PIDs are not placed into the tool-result vault or model context.
- A host-created, run-scoped argument token is required. Host validates exact argument schema `{}`; invalid tokens, wrong scopes, corrupt records, and unwanted fields cannot start an observation.
- Tool outputs are capped to 24 process names / 256 observed visible windows and recorded in the existing **encrypted** DPAPI vault from Phase 3B. Untrusted probe strings cannot become instruction-like names because whitespace, paths and control characters are rejected.
- The model can select a PC observation, mathematical calculation, web search, or normal conversation through the **same** registry. No new persona or memory database is created.

## Privacy boundary — explicit opt-in

**If enabled**, observed application process names are subsequently given to the remote Ollama Cloud provider as part of the agent's tool-observation context. DPAPI only encrypts local storage; it does NOT stop authorized cloud disclosures. Do not enable this on private, sensitive, banking, or shared computer sessions. The environment variable is an explicit, temporary consent gate in the console pilot; it is not a production approval UI.

### Confirm existing general agent still works with PC tools disabled

```powershell
Set-Location "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT\NIRA-V2"
Remove-Item Env:NIRA_V2_PC_OBSERVATION -ErrorAction SilentlyContinue
dotnet run --project .\apps\Nira.V2.GeneralPilot -- "Hi NIRA. Explain a simple calculation."
```

The `Dynamic capabilities` line should show only the existing math/platform/web tools. Nothing should observe the desktop.

### Explicitly enable read-only application name observation for the current shell

```powershell
$env:NIRA_V2_PC_OBSERVATION = "ALLOW_APP_NAMES"
$env:NIRA_V2_MODEL = "gpt-oss:120b"
dotnet run --project .\apps\Nira.V2.GeneralPilot -- "Observe which applications have visible windows on my PC. Also calculate 125 times 24. Report the two observed outcomes; don't guess open app names."
Remove-Item Env:NIRA_V2_PC_OBSERVATION -ErrorAction SilentlyContinue
```

Expected real execution: `TOOL_PROPOSED windows.visible_apps`, `TOOL_SUCCEEDED`, `TOOL_PROPOSED math.calculate`, `TOOL_SUCCEEDED`, then `RUN_COMPLETED` (model choice and order can vary). If the model guesses rather than observing, classify the result as a behavior gap, not a capability success. Use only non-sensitive application sessions.

## Explicit exclusions and next requirements

- This does not yet support file listing/content, clicking windows, window titles, accessibility trees, screenshots, OCR, keyboard input, shell, process launch/termination or filesystem edits.
- Before private-file reading or system control, integrate real per-action user authorization through NIRA's existing approval services; add scoped resource grants, disclosure decisions for cloud models, transaction receipts, crash/restart reconciliation, and independent post-action verification.
- Legacy `NIRAAgent`, `NIRAAgent.UI`, character/memory, voice, ELVARA bridge and all original research pilots are untouched by this release.
- Host opt-in is session-local and should be replaced with a WPF consent UI before production cutover. A developer's console tool is not a complete production security workflow.

**Offline acceptance:** previous 128 tests plus 18 new Windows-tool contract and simulated workflow tests = **146** tests; compiler and live Windows/Ollama verification must be run on the user's Windows .NET 10 environment. Tests with fake observers prove contract enforcement, not the accuracy of every Windows app detection.
