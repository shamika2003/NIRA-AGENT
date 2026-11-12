# Phase 4E.1 — First legacy capability ownership transfer

**Status:** Code prepared; Windows compilation and live authorization verification pending. Phase 4D remains the last proven live integrated route.

## Why this release is a migration, not another assistant

V2 decides when to use a tool. For `legacy.filesystem.read_exact` it cannot select a file path: the WPF host extracts exactly one user-mentioned known-folder path from the current turn and does not expose a `path` field in the provider schema. The tool delegates the read to the **existing** `NIRACapabilityService`, which uses existing contracts, request validation, scoped authorizer, audit store and `NIRAFileReadCapabilityHandler`. V2's encrypted vault retains the bounded observation; there is still one authoritative user memory and character.

The adapter is intentionally in `Nira.V2.LegacyAdapters`, and depends on `NIRAAgent` temporarily. This dependency is tracked as migration debt, NOT considered retired. Once parity is proven, the old executor can be replaced by a canonical single capability service and the legacy orchestration can be removed.

## Security contract

- Opt-in `NIRA_V2_LEGACY_TEXT_READ=ALLOW_EXPLICIT_TEXT_READ` (only for the launched process); requires a current user turn with an explicit reading/review/summarization instruction AND one allowed known-folder-relative path.
- No inherited path from archives, retrieved memories or model decisions.
- Allowed text extensions only; no dotfiles, binary documents, credential/token-marked filenames, links/junctions, folders or files >512 KiB.
- File read limited to 75 initial lines and 3,600 result characters; the model receives an excerpt, not a complete document assurance.
- Authorization and durable audit are performed by the existing NIRA capability service. A failed/uncertain/unaudited/changed-state result is not accepted as successful evidence.
- **Privacy:** Text sent to selected cloud model provider can be sensitive despite the filename filters. Enable only after selecting a non-secret test file; never use on bank data, secrets, credentials or private documents without deliberately accepting external transfer.
- No file writes, process start/stop, browser mutation or desktop input introduced. Running the adapter cannot grant future privilege.
- A file may change between scope validation and the legacy read (TOCTOU). Do not treat this adapter as a hardened sandbox for hostile filesystem actors; stronger file-handle security and per-action consent are still needed before production.

## Test / verification

1. Close NIRA; installer `-AuditOnly`, then `-CheckOnly`. The installer executes the 16 existing V2 test projects plus new `Nira.V2.LegacyAdapters.Tests`; it rebuilds WPF and legacy regression projects.
2. Start `Start-NiraUnifiedPreview.ps1 -AllowPersonalContext -AllowFileObservation -AllowExplicitTextRead`.
3. Test `Read Documents/MULTI-LANG-PROJECT/NIRA-AGENT/NIRA-V2/README.md and summarize the first section. Use the file-reading tool.` A successful result includes a **real tool observation**, not a plausible generated summary. This sends the bounded text excerpt to selected cloud model provider.
4. Test `Check whether Documents/MULTI-LANG-PROJECT/NIRA-AGENT exists` (should not register a text reader; metadata can answer).
5. Confirm WPF Permissions/audit records if a prompt was shown; never assume a declined request succeeded.
6. Use `-LegacyOnly` launch to revert routing immediately, or guarded source revert from the recorded Phase 4E backup folder.

**Do not retire `NIRAExecutive`, `NIRACapabilityService`, identity stores or ELVARA until all 6 retirement gates are proven.**
