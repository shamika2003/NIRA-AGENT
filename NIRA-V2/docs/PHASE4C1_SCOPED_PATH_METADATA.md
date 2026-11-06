# NIRA V2 Phase 4C.1 — First real WPF filesystem observation

This integrates a **general dynamic** filesystem capability into the *existing*
NIRA WPF V2 agent loop. It does **not** replace legacy NIRA, create a chatbot,
change her personality/memory, or modify the ELVARA bridge.

## Host authorization boundary

* Disabled unless `NIRA_V2_FILE_OBSERVATION=ALLOW_REQUESTED_PATH_METADATA`
  is explicitly set before launching the WPF app.
* Enabled for a **single known-folder-relative path directly named in the
  CURRENT `/v2` message**; not from memory, history, websites or model text.
* A model can select `filesystem_check_requested_path` but **cannot supply or
  change its path**: its JSON schema has no parameters.
* Multiple different paths in one request are ambiguous, so no filesystem
  capability is registered. Relative `Documents/ProjectName`, quoted
  `"Documents/My Project"` and the corresponding Windows backslash forms work.
* The host resolves the current Windows user's configured known folder. No
  whole-disk search, arbitrary absolute paths, network shares, or parent
  traversal is enabled. Symlinks/junctions are rejected rather than followed.
* Results contain only the user-typed relative path, existence and whether
  it is a directory or file. No file content, size, modification time,
  directory names, shell command, or state-changing action is available.
* Observations may be transmitted to Ollama Cloud in the next model decision;
  enable this switch only if comfortable sharing the requested path and result.
* The existing legacy `NIRAScopedCapabilityAuthorizer` and WPF approval UI
  remain authoritative for legacy actions. This first read-only grant is
  **separate** and cannot grant V2 write/action permissions. Connecting
  state-changing V2 calls to that authorizer is a future tested milestone.

## Confirmation task

Start WPF with existing V2 canary and personal-context flags, plus:

```powershell
$env:NIRA_V2_FILE_OBSERVATION = 'ALLOW_REQUESTED_PATH_METADATA'
```

Ask NIRA:

`/v2 Check whether Documents/NIRA_R9_TOOL_LAB_01 exists on my PC right now. Do not guess; use a tool observation.`

Then check another explicitly typed path, one at a time, e.g.:

`/v2 Check whether Documents/MULTI-LANG-PROJECT/NIRA-AGENT exists on this PC.`

Expected: a model-selected `filesystem.requested_path_metadata` tool call,
followed by a report tied to the actual observed result. No model can claim
that old remembered folders exist merely because memory mentions them.

## Release gates

1. Exact-source guarded installer audit against the November 6 full-source export.
2. Every previously shipped 184 offline checks plus 17 new file-scope checks.
3. Build GeneralPilot, real NIRA WPF, and legacy regression assembly.
4. Live WPF observation demonstrates actual tool execution.
5. Verify no files created/changed and no other paths exposed in tool result.

Production readiness is **not** established by this release. Permissions UI,
file enumeration/content reads, edits/creates/deletes, app/browser control,
side-effect reconciliation and independent outcome verification remain gated.
