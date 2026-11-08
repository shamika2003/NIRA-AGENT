# NIRA V2 Phase 4H.3 — Guarded goal-linked branches

This is a source candidate, not production acceptance. It extends the independently tested Phase 4H.2 goal-creation baseline. It does not change Git, delete the old executive, migrate the authoritative SQLite DB, or activate a branch runner.

## Capability

- One exact direct user command:
  `Create branch for goal <existing-guid> named "objective" with success criterion "criterion".`
- The parent must be an existing, open, non-blocked authoritative goal. No model-provided goal ID or task ID is accepted as tool arguments.
- Separate opt-in process flag `-AllowBranchCreate`; requires both `-AllowPersonalContext` and `-AllowLifecycleContext`.
- Existing NIRA WPF approval broker must explicitly return Allow Once for the bound parent ID, objective, and criterion; Remember is prohibited.
- Branch is `Required` and starts `Waiting` with a concrete waiting condition. No autonomous work is dispatched by creation.
- Existing `NIRABranchService` and `NIRABranchStore` own the record. A second store **instance** is used solely to read back from the **same existing SQLite database** after the commit.
- A failed/uncertain postcommit readback produces a reconciliation state. Never blindly replay a write on resume or after a timeout.

## Acceptance and rollback gates

1. Clean Phase 4H.2 baseline audit: three original files match their exact manifest source fingerprints; five new files must be absent or already installed. Unfamiliar edits fail closed.
2. Windows atomic replacement preflight; no running `NIRAAgent.UI`; build WPF; run all V2 regression projects; run legacy regressions.
3. Launch with `-AllowPersonalContext -AllowLifecycleContext -AllowBranchCreate`. No branch writes with only `-AllowGoalCreate`.
4. For a fresh branch name, click **Deny** in the owner toast; verify no branch appears in SQLite or Goals tab.
5. Repeat and click **Allow Once**. Verify the exact parent, objective, criterion, `Required` and `Waiting` by SQLite readback. Restart and verify same ID persists.
6. Repeat the same command; registration must refuse a duplicate; test stale/blocked parent ID fails closed.
7. Do not enable goal state transitions, scheduled commitments, automatic branches, memory writes, browser submissions or legacy retirement in this candidate.

If any write runs without a fresh approval or after Deny, stop and revert to the protected Phase 4H.2 baseline.
