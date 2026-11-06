# NIRA V2 release and verification gates

## Phase 0 — Current bootstrap

- [x] New isolated projects; no legacy source dependencies.
- [x] Dynamic decision/action/observation loop with bounded work.
- [x] Checkpoints and event progress saved after real transitions.
- [x] Approval boundary for state-changing actions.
- [x] Suspended uncertain mutations require reconciliation, no auto-retry.
- [x] Research document assembly excludes unsupported claims and unreviewed media.
- [x] Offline synthetic tests included.
- [ ] Compile & execute on Windows .NET 10 (**user's CheckOnly required**).

## Phase 1 — Real single-agent runtime

- [ ] Wire `IAgentReasoner` to configurable GPT-OSS / Ultra provider via typed tool-call JSON.
- [ ] Introduce a model router with configurable roles, fallback, maximum latency and token/cost budgets.
- [ ] Support real outcome-led replanning without predetermined call counts.
- [ ] Store results in encrypted, versioned, transactional persistence; prove restart safety under process kill.
- [ ] Connect one existing NIRA identity and conversation memory through an adapter.
- [ ] Allow task cancellation, user-approval resume and per-run progress streaming in WPF.

## Phase 2 — Evidence-driven research

- [ ] Verified research provider adapters, fetch full documents and detect source conflicts.
- [ ] Claim-level evidence ledger, targeted repair, freshness tracking and source checks.
- [ ] Media provenance, visual scene match, perceptual duplicate rejection, verified source URLs and license metadata.
- [ ] ResponseDocument renderer with inline figures, semantic tables, timelines and optional diagrams.
- [ ] Regression corpus: Apollo 11, JWST, Tesla plus **unseen** subjects; precise factual assertions and manual visual reviews.

## Phase 3 — Capability migration and cutover

- [ ] Side-by-side parity for Windows tools, authorization and verification receipts.
- [ ] Character, emotion/relationship, voice, goals and long-term memory retained with no data duplication.
- [ ] ELVARA bridge contract adapter tested with app scopes and existing authentication.
- [ ] Restore testing, performance profiling and controlled legacy retirement.

**Release rule:** A passing compile/test suite is only an engineering gate. Model quality and real historical image authenticity require live evaluations.
