# NIRA V2 Phase 4H.5 — Exact Authoritative Lifecycle Readback

## What this fixes

Phase 4H.4 allows one approved progress/waiting/blocker mutation, verified
by immediate SQLite readback. A fluent model reply or a branch sidebar label,
however, cannot prove an earlier update survived an application restart.

Phase 4H.5 introduces a **read-only** inspection of exactly one existing goal
or branch GUID supplied in the **current direct user turn**. The existing
NIRAGoalStore / NIRABranchStore remain the persistence owners. The read tool
must cross-check persisted and live service snapshots before exposing any
status or evidence count. It creates no second personal state database.

## Consent and exact commands

Launch with all three flags:
`-AllowPersonalContext -AllowLifecycleContext -AllowLifecycleInspect`

Send exactly one of:

- `Inspect saved branch 8b9c8d6f-2bb1-4d65-9bee-4768606eb230 including status, last progress and evidence count.`
- `Inspect saved goal 40ad8b7b-3a1e-4ad1-965c-cad8723342a9 including status, last progress and evidence count.`

Current-turn source, valid nonempty GUID and the per-process opt-in are
required. The model supplies `{}` only and cannot choose the target.

## Result and failure rules

Success returns the exact type, GUID, status, persisted evidence count, last
stored evidence summary, current waiting/blocker text, bounded description,
update time and branch goal ID, plus `verifiedAgainstLiveState=true`.
The last stored evidence is explicitly user-attested, not necessarily real-world
confirmation. No evidence-history dump, credentials, file content or unrelated
private items are disclosed.

Absent, diverging, malformed, disabled or unavailable reads fail closed and
must never become a guessed success. No Modify tool is registered. No owner
write-approval toast is needed: this is Observe-only with separate process
consent. Read-only results may reach selected cloud model provider.

## Acceptance

1. Build WPF + run all offline V2 and legacy regression suites.
2. After previously recorded branch progress, restart NIRA, launch with the
   three inspection flags and issue the exact branch request above.
3. Verify the tool-derived result has `Waiting`, an increased numeric evidence
   count and the exact last progress phrase. Compare the SQLite row and any
   UI evidence display if desired.
4. Relaunch without `-AllowLifecycleInspect`: no disclosure or fake result.
5. Malformed IDs, unrelated requests, or missing rows must fail without
   modification. No automatic retry or autonomous work is introduced.

## Next gate

Only after this readback passes live: owner-approved terminal branch completion
with explicit result evidence, dependency/work checks and post-commit readback;
then separate goal completion. Never treat model prose as verified execution.
