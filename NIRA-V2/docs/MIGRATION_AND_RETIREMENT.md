# NIRA legacy migration and retirement plan

**Do NOT delete the current app yet.** The uploaded source is an export, not the working Git repository or its persistent runtime data. V2 is constructed without any old three-block or R4-series code. This already provides a clean new-code boundary while the old app continues to function.

## Inventory grounded in source export

- 387 exported code/config files and 16 resource files in `NIRA_AGENT_FULL_CODE(20261105-061322).md`.
- Existing source projects: `NIRAAgent`, `NIRAAgent.UI`, `NIRAAgent.RegressionChecks`.
- Overloaded coordination hotspots: `Mind/NIRAExecutive.cs` (~11.3k lines), `AI/Cognition/NIRACognitionService.cs` (~4.9k), `NIRACognitionPromptCompiler.cs` (~3.6k), and the monolithic regression test `Program.cs` (~12.7k).
- Preserve identity, character, relational state, and memory in their authoritative existing locations.
- Preserve `NIRAAgent/Integrations/Elvara` under Bridge project ownership. No duplication or rewriting without owner-specific work.

## What is removed from the NEW V2 dependency graph now

- No import/reference to legacy `NIRAExecutive.cs`.
- No import/reference to legacy three-block prompt compiler or MiniBlock stages.
- No dependency on R4.x review fallback and source-excerpt delivery.
- No dependency on legacy image-URL-in-Markdown placement or gallery heuristics.
- No dependency on the legacy monolithic test runner.

Nothing is deleted from the existing NIRA repository by this starter.

## Physical retirement gates (must pass before deleting legacy source)

1. **Git safety:** clean, committed backup and restore point; identify persisted files and user data folders.
2. **Parity map:** one-by-one feature inventory of character, memory, voice, desktop capabilities, permissions, UI, goals and bridge communication.
3. **Side-by-side:** V2 executes real tasks with old NIRA in read-only/shadow mode, without shared writes.
4. **Research quality:** independent unseen factual research benchmarks; no raw search-result fallback and supported citations.
5. **Media quality:** authenticated official source retrieval, relevant visual classification and no repeated assets.
6. **Action safety:** restart and interruption tests for every state-changing adapter, including reconciliation receipts.
7. **Persistence migration:** reversible backups, schema versions, encryption, migration dry-runs and rollback.
8. **Cutover:** switch one workflow at a time with feature flags; retain old components until recovery criteria pass.
9. **Removal:** delete/retire old components only after their callers are gone, the full build/regressions pass and backup restores work.

## Suggested order of component replacement

| Priority | Legacy component | V2 replacement / decision |
|---|---|---|
| 1 | Deep research orchestration, source-only fallback, R-series research review rules | `ResearchWorkflow` + evidence/claim validators + structured response document |
| 2 | Search/image downloads and improvised gallery binding | `MediaWorkflow` + typed verified media store + visual relevance and duplicate detector |
| 3 | `NIRAExecutive` mixed tool/runtime orchestration | `AgentLoopEngine` + explicit capability registry and action receipts |
| 4 | Three-block prompt compiler | Small task-specific prompt/contract adapters behind `IAgentReasoner` |
| 5 | WPF mixed Markdown/rich blocks | ResponseDocument-native WPF renderer; keep existing theme and interactions |
| 6 | Legacy test monolith | Domain-specific regression projects and real-quality evaluations |
| NEVER COPY | Original character, memory, privacy or ELVARA bridge state | Reuse via authorized adapters; no parallel persona or memory DB |

This is an architectural **retirement plan**, not a destructive cleanup command.
