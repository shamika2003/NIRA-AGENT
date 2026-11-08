# NIRA V2 — Phase 4H.2 authoritative goal creation vertical slice

## Scope and boundaries

- Uses **existing** `NIRAGoalService`, `NIRAGoalStore`, and NIRA WPF permission broker. No other goal database, executive, model, scheduler or character is introduced.
- ONLY exact current-user creation: `Create goal "OBJECTIVE" with success criterion "CRITERION".` Both quoted values must be 5–180 characters; no multiline instructions or additional commands.
- Starts in **Waiting** with `WaitingFor` explicitly requiring later user activation. This stage does NOT activate work, create branches, modify goals, schedule commitments, form memories, or change character state. It does not treat a goal as a reminder.
- All three independently enabled process flags are required: `-AllowPersonalContext`, `-AllowLifecycleContext`, `-AllowGoalCreate`. A goal proposal is available only if it matches the current `NIRAMindEvent` from the user.
- Model can only send `{}`; exact objective, completion criterion, status and persistence owner are host-fixed. Nothing in old chats, model output or capabilities can grant a write.
- Existing WPF broker shows exact goal/criterion/status; `Allow Once` only. Deny/timeout yields no dispatch; Remember is disabled. Ordinary chat never opens a goal-creation request.
- Existing goal service supplies normalized state, evidence quote validation, deduplication and actual commit. The adapter independently re-reads the goal from the same owner and checks source event, objective, criterion, open state and initial Waiting status. Only verified receipts are advertised as success. Unknown postcommit outcomes are **NEEDS_RECONCILIATION**, never automatically replayed.
- Independent test project `Nira.V2.GoalCreate.Tests`; all V2 suites, legacy regression checks, and WPF build run through the guarded installer after patching.

## Live acceptance (Windows)

Launch from repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\NIRA-V2\scripts\Start-NiraUnifiedPreview.ps1' -AllowPersonalContext -AllowLifecycleContext -AllowGoalCreate
```

Send NIRA exactly:

> Create goal "Verify Phase 4H goal persistence" with success criterion "Confirm the saved goal exists after restarting NIRA".

**Deny first** in the existing Permissions/approval toast. Confirm it did **not** appear in the Goals tab. Repeat and **Allow Once** after checking objective, criterion and **Waiting** status. Confirm exactly one new goal in the Goals tab. Close/restart NIRA, ask "Show my current goals", verify the same record and status. Repeat the exact request: NIRA must say that goal already exists; no duplicate is created and no second write is dispatched. An ordinary RAM/SSD question must not mutate state or ask for authorization.

Further negative tests: arbitrary unquoted text, appended semicolon command, lowercase or missing consent flag, and denied action. Do **not** approve an unrelated operation.

## Known incomplete production gates

This is **4H.2 create-only acceptance**, NOT full 4H.2–4H.5 cutover. Exact goal changes, branch writes, scheduled commitments, memory formation, character ownership, and cross-event one-writer cutover remain disabled pending separate tests. The browser source/citation issue remains parked until production parity. Do not remove legacy executive, databases, personal memories or scheduler.
