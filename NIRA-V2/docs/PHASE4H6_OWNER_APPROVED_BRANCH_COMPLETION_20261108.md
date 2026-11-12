# NIRA V2 Phase 4H.6 — Exact owner-approved terminal branch completion

This is a LIMITED capability migration slice, NOT a declaration that the
legacy executive may be removed or that arbitrary branch work is complete.

## Current owner and scope

- Existing `NIRABranchService` remains the only writer and uses the existing
  SQLite branch/goal database. WPF uses the existing permission broker; no
  new permanent personal store, identity, goal creator, or scheduler is added.
- Start NIRA with `-AllowPersonalContext -AllowLifecycleContext -AllowBranchCompletion`
  (optionally `-AllowLifecycleInspect` for independent follow-up readback).
- The exact current **direct user** instruction must be:

  `Complete branch <GUID> with result "<12..240 characters>" and evidence "<12..300 characters>".`

- The model can only propose the tool with `{}`. It cannot set the branch ID,
  result or evidence. One fresh owner **Allow Once** is required; remember is
  forbidden. Without all gates, no modifying tool is registered.

## Safety and truthful completion

Before approval AND immediately before execution, read-only live and SQLite
snapshots must match. Require an open `Waiting` or `Active` branch and open
parent goal, completed prerequisite branches and REQUIRED children, and **no
assigned work that is not Succeeded**. This blocks pending, running, failed,
rejected, authorization-required, interrupted and cancelled work. Branch
service performs its own dependency checks again while committing.

A successful owner write is NOT success until SQLite and live snapshots agree
on exact branch, parent goal, terminal `Completed` state, new evidence count,
result, last evidence and resolved timestamp. Failures after possible commit
require reconciliation and MUST NOT be retried automatically. A second request
cannot re-register completion for a resolved branch.

The request's quoted evidence is **user-attested**; it cannot automatically
prove the real-world result. Owner must check the actual completion criterion
and observed records before approving. Existing branch-resolution listeners
remain intact, but this tool does not dispatch work or mutate a goal directly.

## Intended Phase 4H.6 live acceptance

1. Close the NIRA process. Run guarded package audit and ApplyAndVerify (which
   builds WPF and runs the new and existing tests, rolling back on error).
2. Launch only after ApplyAndVerify passes. Use three lifecycle switches above.
3. On the verified restart-persistence test branch, submit one explicit,
   user-attested completion statement and review its exact Allow Once popup.
4. Confirm independent SQLite/live readback produces `Completed`, evidence
   count incremented exactly once (the previous inspection showed **2**),
   identical branch and goal IDs, and correct result text.
5. Restart NIRA with `-AllowLifecycleInspect`; inspect the exact branch ID.
   The status must still be `Completed`; a repeated completion must fail closed.
6. Verify denial, absent flags, extra untrusted arguments, missing/dependency-
   blocked work and uncertain postcommit simulations in automated tests.

## Next planned slice

Phase 4H.7 would add a separately approved **goal** completion with dependency
validation and authoritative reconciliation. Until then, a completed branch
must never be reported as automatic goal completion.
