# NIRA V2 Phase 3B — Protected state for the general agent

## What is integrated

- The existing `Nira.V2.GeneralPilot` now uses the same agent loop and same dynamic capability registry, but its run checkpoints AND opaque argument/result vault are protected with Windows CurrentUser DPAPI.
- State moves from `%TEMP%\NIRA-V2-GeneralPilotRuns` to `%LOCALAPPDATA%\NIRA\V2\GeneralPilotSecure`. Existing plaintext test runs are NOT automatically migrated or removed.
- Purpose-bound DPAPI entropy separates each checkpoint and each run+tool+role+payload object. The binary envelope has a version marker. Altered ciphertext or a wrong scope fails closed; no plaintext fallback exists.
- The existing `FileAgentStateStore` and `AgentPayloadVault` constructors remain compatible with the public-only Phase 0–3A demos/tests, which are intentionally unchanged.
- The existing checkpoint lease and atomic file replacement are retained. Encrypted state is loaded on resume, but crash/restart and write reconciliation still need larger operational testing.

## Threat boundary and limitations

- DPAPI protection at rest protects against casual disk disclosure in a different Windows user context. **It does not defend against malware running as the same user**, privileged administrators, compromised Windows profiles or untrusted model providers.
- Cloud models receive the text explicitly sent to them. Encryption at rest does not make prompts private from the provider.
- The general pilot still exposes only read-only math, basic platform info and optional public web search. It is NOT an approved tool for private bank files or passwords, and has no desktop writes or browser control.
- DPAPI data is bound to your Windows profile/master keys. **Copying encrypted files to another PC will not reliably restore them.** Production requires an explicit, tested account recovery/backup and schema-migration plan.
- This is versioned *envelope encryption*, not a complete versioned transactional database, scheduled retention service, or production memory store.
- Old unencrypted research and live pilot checkpoints are unaffected. Review and delete those manually after confirming they are no longer needed; don't use blanket Temp cleanup.
- ELVARA bridge, original NIRA identity/memory/UI, original capabilities, and legacy app source are untouched.

## Verification

Run the guarded installer with `-AuditOnly` and `-CheckOnly`. It must pass all existing 114 checks plus the new protected-store regression tests. The Windows-only DPAPI round trip is exercised on the user PC by the new test suite.

Then rerun `Nira.V2.GeneralPilot` with a **non-sensitive** ordinary conversation and a two-tool research+calculation prompt. A successful run prints `[PRIVATE STATE]` and persists encrypted checkpoint bytes under LocalAppData. The model can still choose tools dynamically; encryption does not change its routing.

## Next production work

1. Read-only Windows capability host adapters with explicit scope and observation redaction.
2. Authorization and per-action operator consent, including re-observation and verified mutation receipts.
3. Conversation/identity/memory integration using the existing NIRA sources, not a second identity database.
4. Transactional persistence with schema evolution, recoverability, retention, backups, crash stress, red-team testing and WPF integration.
