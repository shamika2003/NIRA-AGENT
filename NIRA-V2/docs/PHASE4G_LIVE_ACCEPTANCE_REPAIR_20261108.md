# NIRA V2 Phase 4G — Live Acceptance Repair Candidate (2026-11-08)

Scope: targeted repair only. No Git operations, no legacy deletion, no broader tool privileges.

## Observed defects and treatment

1. Host-scoped browser tool: a model call with an extra URL argument was rejected with `TOOL_ARGUMENT_SCHEMA_INVALID`. Allow one *model-only* correction for an observed browser tool with empty schema. Never execute the malformed call, never accept model-supplied navigation URLs.
2. Desktop window activation: actual Win32/legacy audit showed `Dispatched=False`, `ChangedSystemState=False`, `OutcomeUncertain=False`. The V2 tool now sends an explicit **confirmed-no-change** receipt, and the run terminates without replay. Genuine unknown outcomes continue to require reconciliation.
3. Explicit DOCX/TXT paths without a registered tool: host provides deterministic admission feedback rather than letting the model imply it has no desktop tools or claim nonexistent file access. The reason may be a missing file, existing target, permissions, or missing startup flag. No new permission is granted.
4. Incomplete/denied mutation UI messages: distinguish no-effect, not-approved, and uncertain outcomes. Never report success without audit and independent evidence.

## Windows acceptance after full build

- Launch NIRA with **only the intended permissions**, including `-AllowTextCreate` for the TXT test and `-AllowDocxInspection` for existing Word documents.
- Create a *fresh, previously nonexistent* benign text file. Check that Allow Once is shown and inspect exact path/content before approval.
- Repeat with a different fresh file, then actually click **Deny** in the broker. Its file must be absent. Words in quoted file content such as `Do not save this` do **not** deny authorization.
- Browser: `Visit https://example.org/ in the browser, then inspect the active page.` Must actually observe the page, or honestly report an unavailable browser.
- DOCX: create a fresh Word document after Allow Once. Then inspect **that same existing file**. An attempted second creation at its path must refuse overwrite and retain the original SHA-256.
- Desktop: inspect `application Code` and request activation. Windows may deny focus under foreground restrictions. A verified no-dispatch/no-change must report no effect, not NEEDS_RECONCILIATION. Do not retry automatically.
- Verify filesystem receipts and desktop audit independently. Stop on an unexplained write or missing approval.

## Known limitations

No browser form submission, arbitrary desktop typing/clicking, screenshot-to-model vision or microphone migration is added. Full Phase 4G parity and subsequent retirement remain pending. This candidate needs Windows .NET test and WPF build verification before live use.
