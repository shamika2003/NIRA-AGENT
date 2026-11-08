# NIRA V2 Phase 4H.4 — Authoritative Lifecycle Updates (Controlled Pilot)

## Scope

One V2 host-authorized current-turn mutation of an EXISTING NIRA goal or branch using
existing `NIRAGoalService` or `NIRABranchService` and their shared SQLite store.
No second executive, scheduler, database, memory, or permission authority is created.

Supported user commands (quotes required, exact GUIDs):

- `Record progress on branch <GUID> saying "Observed work or evidence".`
- `Record progress on goal <GUID> saying "Observed work or evidence".`
- `Set goal <GUID> to Waiting because "Concrete waiting condition".`
- `Set branch <GUID> to Waiting because "Concrete waiting condition".`
- `Block goal <GUID> because "Concrete blocker".`
- `Block branch <GUID> because "Concrete blocker".`

These are user-attested entries; they do not independently prove real-world task completion.
NIRA may not infer authorizations from chat history or memories.

## Security and execution contract

- Three process-local host gates: `-AllowPersonalContext -AllowLifecycleContext -AllowLifecycleUpdate`.
- Exactly one current, direct-user message binds item type, GUID, action and evidence text.
- WPF's existing authorization broker asks for a FRESH **Allow Once** approval, with
  the exact old status and evidence count. Remember approvals are disabled.
- The model supplies only `{}`. Model-generated IDs, status changes, or reason strings are rejected.
- Existing items must be open; no Waiting no-ops, no repeated Blocked status, no
  reactivation of a previously Blocked item through this tool.
- Identical consecutive progress summaries are rejected to reduce duplicate entries.
- An item-state/evidence-count change between registration and approval or execution
  causes rejection; stale approvals do not silently attach to newer state.
- Existing services validate user-event evidence and commit the change. A separate
  read of SQLite must confirm exact item, new status and evidence counter increment.
- Ambiguous post-commit exceptions or missing readback require reconciliation,
  **NEVER** an automatic replay.
- Progress, Waiting and Blocked only: there are NO completion, cancellation,
  activation, reminder, timer, filesystem, browser or autonomous work powers.

## Live acceptance

Using the verified existing branch ID from Phase 4H.3:

`8b9c8d6f-2bb1-4d65-9bee-4768606eb230`

1. Launch with the three consent flags. Request:
   `Record progress on branch 8b9c8d6f-2bb1-4d65-9bee-4768606eb230 saying "Verified branch is visible in the sidebar".`
2. Click **Deny** in the permission dialog. No evidence count or last evidence summary must change.
3. Repeat and **Allow Once**. Verify the branch remains Waiting, the evidence count increases
   by exactly one and the persisted summary matches the quoted text.
4. Restart NIRA and confirm persistence in the existing branch state.
5. Attempt the identical action without the launch flag: it must report disabled and not modify state.
6. Use a fresh test branch/goal for Waiting and Blocked transitions if desired. Do not block
   a valuable live goal solely for testing; blocked execution is intentionally not resumed here.

## Next release gates

Goal/branch activation, completion/cancellation, temporal commitments, memory formation,
character ownership, duplicate reconciliation across restarts, and full 4G browser parity
remain outside Phase 4H.4. Legacy retirement stays blocked.
