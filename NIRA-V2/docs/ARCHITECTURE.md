# NIRA V2 — Architecture decision record (foundation release)

**Status:** V2 foundation implementation, NOT a replacement for the running WPF app.
**Source basis:** `NIRA_AGENT_FULL_CODE(20261105-061322).md` (387 exported code/config files), `NIRA_MASTER_ROADMAP_2026-10-12_REV12.md`, and `ELVARA_THREE_PROJECT_OWNERSHIP.md`.

## One identity, dynamic execution

```text
Existing WPF Chat / ELVARA channel (adapter, later)
                    |
          One NIRA identity service
       (personality, social context, memory)
                    |
              AgentLoopEngine
     decision -> tool -> observation -> decision
       |           |           |
   reasoner    registry     progress journal
  (provider     + gate       + checkpoints
   adapter)       |               |
       +----- verified execution receipts ----+
                    |
        Evidence + artifact services
                    |
        Structured response document
                    |
           WPF renderer (later)
```

The runtime does not define multiple agent personalities. Models (e.g., GPT-OSS, Ultra, vision) are **replaceable providers**, selected via model-router adapters later. Tool execution is mediated by registered capabilities and permissions. This foundation does **not** call any live model or web service.

## Shipped, implemented modules

| Module | Owns | Current maturity |
|---|---|---|
| `Nira.V2.Contracts` | Serializable agent decisions, run state, activity events, evidence/claims/figures | Implemented; schema migration pending |
| `Nira.V2.Runtime/AgentLoop` | Bounded iterative agent execution, permission gate, durable checkpoints, restart behavior | Implemented offline; not yet production-hardened |
| `Nira.V2.Runtime/WorkflowEngine` | Deterministic dependency workflows for predictable operations | Existing separate foundation path |
| `Nira.V2.Research` | Structured answer document trust/assembly gate | Implemented offline; external source verification absent |
| `Nira.V2.AgentLoop.Tests` | Synthetic scenarios for agent runtime, restart, failure and safety | Source tests; Windows execution required |
| `Nira.V2.Demo` | Visible event-by-event example of the orchestration loop | Synthetic only |

## Execution invariants (non-negotiable)

1. A model may request a registered capability; it cannot invent a capability or bypass authorization.
2. Side-effecting operations require an idempotency/reconciliation key. **A key alone never makes retries safe.**
3. The engine writes a `TOOL_DISPATCHED` checkpoint **before** executing a tool.
4. If restarted while a write might have run, the engine enters `NeedsReconciliation` and does not replay it automatically.
5. Read-only failures are recorded as observations, not converted into user-facing web excerpts.
6. All execution loops are bounded; an incomplete run has an explicit status and reason.
7. Progress events describe actual persisted execution transitions, not invented model narrative.
8. A claimed source is not a verified fact. Evidence records must be reviewed before associated claims can be published.
9. A file's institutional origin alone does not prove the depicted scene. Media needs provenance + visual review + uniqueness.
10. A WPF render failure must never destroy or rewrite a validated research document.

## The single-answer document boundary

`ResponseDocument` is an intermediate representation, with sections, paragraphs, citations and figures. Figures are referenced by ID and anchored to their section, never inserted by Markdown text-search heuristics. The renderer must resolve those IDs against a verified media store. Unreviewed claims and unreviewed media are omitted by the builder. This is a validation gate, **not** a substitute for a real evidence reviewer.

## Source of truth and ownership

- Existing NIRA owns character, long-term memory, goals, desktop UI and general cognition.
- ELVARA bridge code remains **bridge-owned**, even when under `NIRAAgent/Integrations/Elvara/`. No bridge files are copied or edited by this foundation.
- TradeAI execution, business logic and authoritative data remain TradeAI-owned.
- No new identity, relationship state or duplicate memory database is introduced.

## Remaining mandatory production work

- Secure, transactionally durable persistence with schema migration and encrypted secrets.
- Durable encrypted tool-input vault; this foundation only stores **references** to inputs.
- Provider adapters with tool schemas, retry classification, costs, cancellation and context compaction.
- Evidence retrieval, source provenance, claim-level reviewer and targeted factual repair.
- Verified media repository with official archive provenance, visual checks and perceptual deduplication.
- Structured WPF renderer, streaming activity panel and accessibility support.
- Host integration with existing NIRA identity, authorizations, conversation, memory and ELVARA scope.
- Operational performance, fuzz/security, offline and online evaluation, crash, race and migration tests.

**Production gate:** none of these may be declared working solely because the foundation's synthetic tests pass.
