# Phase 4D — Single host entry, V2-first read-only preview

## Outcome
NIRA's **existing** WPF host is the only user-facing chat application. The
previous `/v2` command is no longer required *when* the operator explicitly
selects `NIRA_AGENT_RUNTIME=V2_DEFAULT_READONLY` at startup.

There are still two internal execution engines for now. V2 handles eligible
ordinary text and chooses from the same dynamic registry validated in Phases
3A–4C.1. Legacy NIRA continues to own ELVARA messages, internal/autonomous
work, image attachments, attached past chats, selected-file attachments,
long-term memory writes, character mutation, and non-migrated capabilities.
There is NO task-keyword routing. A plain-text request for a capability not
registered in V2 is **not** secretly passed to the legacy executor, because
that would hide missing permissions and could cause duplicate side effects.
The V2 runtime must explain the limitation; in the interim start `LEGACY_ONLY`
for full legacy functionality. File metadata, PC application names and personal
context each still require their **independent** consent switches.

## Runtime modes (exact, host-owned; not model authored)

* `NIRA_AGENT_RUNTIME` absent: original behavior. Regular chat uses legacy.
  Explicit `/v2 ...` uses V2 only if `NIRA_V2_WPF_CANARY=EXPLICIT_READONLY`.
* `NIRA_AGENT_RUNTIME=V2_DEFAULT_READONLY`: normal eligible user text uses V2
  without `/v2`; current V2 read-only tools only. `/v2` is also accepted for
  backward compatibility.
* `NIRA_AGENT_RUNTIME=LEGACY_ONLY`: all user text, including `/v2`, uses legacy.
* Other/malformed `NIRA_AGENT_RUNTIME` values fail closed for V2.

These are migration switches, not the long-term product UX. The supported
launcher `scripts/Start-NiraUnifiedPreview.ps1` scopes them to its process,
restores the prior environment afterward and never edits persistent settings.
Use the `-LegacyOnly` switch for a quick no-V2 launch. Do not delete legacy
modules, memories, backups, or authorization services yet.

## Validation

Run the installer first (`-AuditOnly`, then `-CheckOnly`). It checks all 16
existing V2 test projects, including **20 new** unified routing checks added
to `Nira.V2.WpfBridge.Tests`; then builds the real WPF executable, GeneralPilot
and legacy regression assembly. Expected **221 passing offline checks**;
Windows execution is the definitive compiler gate.

Run the new launcher (from your NIRA repository root):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\NIRA-V2\scripts\Start-NiraUnifiedPreview.ps1" -AllowPersonalContext -AllowFileObservation
```

Then in NIRA's WPF Chat (NO prefix):

```
Hello NIRA, how are you today?
Calculate 125 * 24 and explain.
Check whether Documents/MULTI-LANG-PROJECT/NIRA-AGENT exists on my PC right now. Use a real observation.
```

Check UI answers and local runtime progress. Enabling `-AllowPersonalContext`
permits selected history/memory/personality to reach the selected cloud model provider model.
`-AllowFileObservation` permits one current-turn path status to reach the model.
No file contents, directory listings, shell or write operations are connected.

## Production gates still OPEN

1. Migrate real authorized filesystem, browser, desktop and app operations
   into the same V2 registry without bypassing NIRA's existing approval broker.
2. Integrate full goal/branch/autonomy and audited long-term memory formation.
3. Verify crash/resume, action reconciliation, cancellation, concurrency,
   telemetry, WPF progress and source/domain grounding under realistic tasks.
4. Move runtime selection to the existing NIRA settings UI only after parity;
   remove canary compatibility; retire legacy orchestration only after a
   recoverable, measured cutover.

The goal is ONE NIRA with one active execution engine, not one giant .cs file.
No ELVARA bridge/TradeAI-owned source is changed by this release.
