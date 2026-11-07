# Phase 4E.3 — Capability cutover catalog and read-only file integrity

## Purpose

Phase 4E.2 showed the existing NIRA legacy `filesystem.list` service can be called by the new V2 general agent with a real live audit receipt. Phase 4E.3 adds one more **real delegated capability**, `filesystem.hash`, as the first prerequisite for future independent verification of writes. This is not an extra AI mind or duplicate filesystem implementation.

## Current WPF V2 capability inventory

| V2 capability | Underlying owner | Risk | Consent and scope | Status |
| --- | --- | --- | --- | --- |
| Calculator + platform observations | V2 local built-ins | Observe | Registered | Existing |
| Web search | V2 Tavily | Observe | Provider key, model tool choice | Existing |
| Windows app-name observation | V2 Windows adapter | Observe | Separate launch consent | Existing |
| Exact requested path metadata | V2 Filesystem | Observe | Exact current-turn path + separate launch consent | Existing |
| Exact text excerpt | NIRA legacy capability service | Observe | One allowlisted path + separate launch consent + legacy audit | Existing 4E.1 |
| Exact directory listing | NIRA legacy capability service | Observe | One directory + separate launch consent + legacy audit | 4E.2 live tested (empty folder) |
| Exact SHA-256 text/code file | NIRA legacy capability service | Observe | One file + separate launch consent + legacy audit | New 4E.3: live acceptance pending |
| Create, append, overwrite, edit, delete | NIRA legacy capability service | Modify/Destructive | NOT exposed to V2 | Phase 4F or later |
| Browser/desktop mutation, voice, images, goals | Legacy NIRA or subsystem owners | Mixed | Existing authority retained | Further phases |

## Engineering invariants

1. One V2 decision loop, one existing NIRA capability/permission/audit authority; no direct model filesystem path or local path guessing.
2. Current user request must name exactly one known-folder relative file and explicitly request SHA-256/hash/checksum/integrity. Memory cannot grant scope. Host maps actual current-user known folders.
3. `-AllowFileHash` is separate from `-AllowDirectoryListing`, `-AllowExplicitTextRead`, and all other consents. It enables only this **Observe** tool; it is never write consent.
4. Only non-sensitive allowlisted text/code filenames, regular files up to 64 MiB; no symlinks/junctions or credentials; no file contents returned to the model.
5. Delegated hash result must be audited, read-only, for the exact request ID and exact path, complete, with a 64-character hexadecimal digest and measured file length.
6. No successful model reply proves a file was mutated. Normal V2 routes still reject all modifying tools.

## Phase 4E.3 acceptance

- Run `Nira.V2.FileHashDelegation.Tests`, existing 4E tests, all 522 legacy regressions, and WPF build.
- For a non-sensitive test file under `Documents/TestFolder`, get the actual PowerShell SHA-256 and use the launched V2 to ask for its exact SHA-256 without `/v2`. Results must match.
- Negative tests: no launch consent, wrong case flag, model-supplied path, ambiguous two paths, traversal, unsafe filename, altered audit/request/capability ID, legacy write receipt, corrupt hex, wrong file path, fake success.
- Re-run directory listing with the newly created test file to prove nonempty observations. Remove only your own test file manually after testing.

## Next: Phase 4F — first safe modifying action

Do NOT just change the model's `MUTATION_NOT_ENABLED_IN_PILOT` guard to allow writes. Implement a distinct, explicit WPF owner-confirmed action profile; a host-owned exact path and bounded content; NIRA's existing authorization; create-only (no overwrite) atomic file write; committed checkpoint/idempotency key; independent hash and content read-back; crash-after-dispatch reconciliation; no automatic duplicate writes on restart; pause/deny/revoke behavior; end-to-end tests including interrupted writes and WPF approvals. Only after that first vertical slice can V2 accept another mutation profile.

The end state is still one product and one agent—not two chatbots. The temporary LegacyAdapters boundary is removed only after NIRA's V2 capabilities and personal lifecycle fully own their respective functions.
