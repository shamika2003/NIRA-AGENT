# Phase 4E.2 — Existing NIRA directory-listing transfer

**Project:** Single NIRA production migration, Gate 2 of 6. Not a separate assistant or standalone tool.

- The normal WPF chat sends ordinary text to the existing V2 agent loop through the Phase 4D unified-entry preview.
- Under an independent per-launch owner consent and an explicit current-user-turn listing request containing exactly ONE known-folder relative directory, the model may select `legacy.filesystem.list_exact`.
- Model tool arguments must be `{}`. The host fixes the canonical path, glob `*`, recursion `false`, offset `0`, maxEntries `30`, and uses the existing `NIRACapabilityService` and `filesystem.list` handler for authorization, execution, and audit.
- After successful audited execution, the adapter validates every legacy output record and removes absolute paths and sensitive/hidden entry names. Only immediate relative names and file sizes enter the protected V2 observation vault. Untrusted names are never instructions.
- Failed legacy authorization, uncertain receipts, unaudited results, changed-state flags, malformed listings, escaped paths or unsafe link traversal do not become successful observations.
- No writes, browser operations, shell commands, recursion, pagination or access to a second directory are enabled. This is a limited first page and cannot prove directory completeness.
- Text-file reads continue to require their own `-AllowExplicitTextRead` consent and existing path restrictions. A directory listing does not grant permission to read its files.
- Existing NIRA memory/character, UI, ELVARA bridge and store owners are unchanged. Model calls and consented listing observations leave the computer for selected cloud model provider.

## Live gate

1. Close any open NIRA instance. Install with the guarded script, `-AuditOnly`, then `-CheckOnly`. The installer must run all 252 previous checks plus the new directory suite, build the WPF app and the legacy regression project, and revert exact source bytes on failure.
2. Launch: `Start-NiraUnifiedPreview.ps1 -AllowPersonalContext -AllowDirectoryListing` (and other opt-ins only as needed).
3. Ask: `List the files and folders directly inside Documents/MULTI-LANG-PROJECT/NIRA-AGENT/NIRA-V2. Use your directory listing tool.` The answer should name actual first-page immediate children and note that the result is bounded.
4. Check local diagnostics for `[TOOL-RESULT] Id=filesystem.list Status=Succeeded Risk=Observe Changed=False ... Audit=<id>`. Do not expose private logs or keys.
5. Request an unauthorised different directory in a new message, or relaunch without `-AllowDirectoryListing`, and confirm the tool is unavailable. A remembered path alone must not enable it.

**Retirement gate remains open:** the old NIRA executive is NOT safe to remove. The next capability work must connect guarded writes, verified read-back, browser/app/vision tools and lifecycle integration without duplicating the authority database or creating a second mind.
