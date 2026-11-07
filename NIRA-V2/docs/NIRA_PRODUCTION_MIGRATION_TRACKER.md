# NIRA production cutover tracker

**Baseline:** source export `NIRA_AGENT_FULL_CODE(20261106-090935).md` (~522 source/config files and 16 resources); NIRA V2 Phase 4D running via WPF; source of truth for gates, not a percentage guessed from offline tests.

| Gate | Owner migration | Status on 2026-11-06 | Acceptance criteria |
| --- | --- | --- | --- |
| 1. Unified entry | Default natural chat → V2 engine | **LIVE PREVIEW VERIFIED** (4D); final cutover pending | Normal chat without `/v2`; legacy kill switch; no duplicate archive writes; WPF parity |
| 2. Capabilities and authorizations | V2 tool contract → **existing** NIRA permission/audit/capability service | **IN PROGRESS** (4E.1 text read live verified; 4E.2 delegated directory listing LIVE VERIFIED (empty-folder case)) | Read/list, create/edit, process, app, browser, document actions integrated; per-action approvals; no bypass or double execution |
| 3. Durable multistep execution | V2 checkpoint → verification → recovery/reconciliation | **NOT VERIFIED FOR WRITES** | Multi-step workflows, crash/restart safety, no automatic replay of uncertain writes, independent read-back receipts |
| 4. Multimodal / interaction | Existing voice, vision, screen, browser, companion orb | **NOT MIGRATED** | Same natural V2 run controls visual, vocal and PC capabilities; accurate action/observation |
| 5. One personal lifecycle | Existing character, memories, goals, branches, autonomous time | **PARTIAL READ-ONLY** (4B/4B.2) | One authoritative state write-path; recall, formation, scheduler/goal fidelity and rollback |
| 6. Cutover and cleanup | Remove old executive / duplicate prompt machinery and pilots | **BLOCKED UNTIL 1–5 PASS** | Feature-parity and security suite, WPF/ELVARA tests, recovery exercise, clean solution and external archived backups |

## Meaning of progress

- **Gate 1:** normal text routed to V2 by host preview. Not proof legacy can be deleted.
- **Gate 2:** Phase 4E.1 transfers one real authorized *read* to V2; most capabilities and all state-changing operations remain to migrate. Do not count Gate 2 done from this package.
- **Gates 3–6:** remaining significant engineering work; not a promise of a fixed number of patches or calendar duration.

**Permanent target:** one NIRA mind and execution lifecycle, one memory/identity owner, one authorization service, one WPF/ELVARA integration, one observable job history. Separate C# projects are acceptable modules; duplicate independent agent executives are not.

## Next work sequence

Phase 4E.2: directory listing through the same authoritative legacy service (source code prepared; Windows gate pending). Next: 4E.3 capability catalog and safe first write profile. Phase 4F: action authorization + write reconciliation for exact permitted operations. Phase 4G: browser, vision, apps, voice/companion via V2. Phase 4H: memory formation, goals/background and cross-session lifecycle ownership. Phase 4I: reliability, parity, secure state migration. Phase 4J: **conditional** retirement of old executive + cleanup, only after all gates pass.

## Phase 4E.2 directory listing (pending live gate)

- One current-turn exact known-folder relative directory with separate per-launch consent. No model-controlled path, recursion, filter or pagination.
- Delegates `filesystem.list` to the existing NIRA `NIRACapabilityService`, with its authority/audit checks. No second permission store.
- Host validates request, receipt and output; shows only bounded immediate relative names. No absolute paths, secret-like or hidden entries.
- 4E.1 read and 4E.2 list may be combined in one V2 run only when their *independent* consents and scopes exist.
- This does not complete Gate 2 and does not enable PC writes.

## Phase 4E.3 — first integrity prerequisite (read-only)

- `legacy.filesystem.hash_exact` delegates the existing audited `filesystem.hash` to NIRA's single authoritative legacy capability service. The host selects ONE current-turn known-folder file; the model supplies NO path argument. Independent opt-in is required.
- Eligible files are non-sensitive text/code names only, up to 64 MiB. The cloud receives the SHA-256 digest, byte count and safe relative label, not the file contents or absolute path.
- This is NOT a write permission. The V2 agent loop and model currently reject modifying tools. Phase 4F must add: authorized create-only profile; atomic result; deterministic reconciliation; independent readback/hash and no replay after unknown outcomes; WPF consent handling; real negative/restart tests.
- Acceptance: 4E.3 offline security tests, WPF/legacy regressions, one nonempty-folder live SHA test, and owner-visible hash comparison with PowerShell. Do not count Phase 4E.3 live verified before that test.
- The permanent target is one NIRA runtime and one capability/authorization service; these are temporary adapters, never a second personality, memory database, or execution authority.
