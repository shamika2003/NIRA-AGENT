# NIRA V2 Phase 4H — Personal lifecycle ownership bridge (candidate, 2026-11-08)

## Goal

Make a *single* future NIRA runtime understand the personal projects, open goals,
work branches and active time commitments held in NIRA's **existing** state owners.
No new memory database, second executive, authorization broker, or model-managed
scheduler is created. The old NIRA core remains installed until the full cutover
and rollback gates are proved on the Windows host.

## What this candidate actually implements

- Existing `NIRAGoalService.CurrentGoals` is the authoritative goal source.
- Existing `NIRABranchService.CurrentBranches` is the authoritative work source.
- Existing `NIRASelfModelService.CurrentCommitments` is the authoritative
  commitment/reminder source (including persisted `Temporal.NextWakeAtUtc` when present).
- Existing persona, relevant long-term memories and prior conversation are still
  supplied solely by `NIRAV2ReadOnlyContextBuilder`.
- Every lifecycle snapshot is read-only, single-turn, bounded to 1,750 characters,
  labeled as data rather than instructions, and merged under the existing model
  personal-context limit (4,400 characters).
- A lifecycle snapshot is exposed only when all three hold: **both** independent
  process-local opt-ins, and a current-turn lifecycle-relevant request. No model,
  old chat message or retrieved memory can grant either flag. No lifecycle state is
  read for ordinary unrelated messages.
- No personal write operation is newly available. `AllowOnce` for files and desktop
  does not authorize memory/goal/branch/commitment changes. No scheduler is altered.

## Windows verification and live test

1. Close NIRA before installation. Run `Apply-Nira-V2-Phase4H.ps1 -AuditOnly`
   from the downloaded ZIP, then `-ApplyAndVerify` using the same installer.
   Audit must match the exact current-source baseline, and verification must pass
   the new lifecycle suite, all available V2 console test suites, legacy regressions
   and the WPF build. If not, the installer attempts rollback of its changed files.
2. Launch from project root with `-AllowPersonalContext -AllowLifecycleContext`.
   The launcher does not persist the two consents. It does not alter Git.
3. Ask NIRA **`What are my current active goals, open work branches, and scheduled commitments? Use the actual stored state, and don't invent missing ones.`**
   Compare its answer to the Goals tab and existing commitment display. It should
   distinguish goals from branches and accepted commitments; only actual persisted
   wake dates are evidence of scheduled work. If there are no active entries, it
   must say the *current* snapshot is empty, not that the user has never set goals.
4. Ask an unrelated question such as `Why does an SSD differ from RAM?` The
   lifecycle snapshot is not available for that turn even when flags remain set.
5. Relaunch **without** `-AllowLifecycleContext`, ask the first question again,
   and ensure no private lifecycle state is leaked. `-AllowLifecycleContext` alone
   (without `-AllowPersonalContext`) also exposes none. The prior read-only memory
   context behavior is unchanged when only `-AllowPersonalContext` is supplied.
6. Verify no goal/branch/commitment has changed simply from asking or answering.
   Do not transmit sensitive goal names to a cloud model unless you accept it.

## What is deliberately NOT complete

**Phase 4H is not done merely by passing this candidate.** Before legacy
executive retirement, the following still require explicit engineering and tests:

- 4H.2: transfer memory formation/consolidation, deduplication, recall updates,
  sensitivity gates and persist-once semantics through the **existing** memory
  store. No duplicate writes by legacy and V2 after cutover.
- 4H.3: goal and branch proposals, evidence checks, status transitions,
  delegation and post-restart reconciliation through the existing services.
- 4H.4: persistent commitments and actual timed wake/recovery by the existing
  temporal scheduler; test due, overdue, recurring, cancellation, crash/restore.
- 4H.5: character/self model changes and lifecycle consistency across text,
  voice, PC and other routes; one writer per event and durable audit.
- 4I: whole-app parity, migration rollback, Windows multi-session and outage
  tests, and repair the parked browser citation issue before full parity claim.
- 4J: conditionally remove the legacy executive and duplicated prompt/planner
  machinery only after all required production gates pass and backups exist.

A service adapter delegating to the existing owner is a temporary bridge, not a
second agent. Do **not** remove or rewrite legacy source as part of this package.
