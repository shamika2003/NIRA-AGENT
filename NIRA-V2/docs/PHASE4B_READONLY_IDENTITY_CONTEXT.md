# NIRA V2 Phase 4B — Authoritative identity and continuity (read-only canary)

This is **not** a new NIRA identity, persona, conversation store, or memory database.
The original NIRA remains the sole owner of its existing character state, social history,
conversation archive, and long-term memory. Phase 4B provides a *read-only bounded view*
of selected existing state to the V2 general model, only after explicit process-local consent.

## Consent gates

Both must be set **before launching WPF NIRA**:

- `NIRA_V2_WPF_CANARY=EXPLICIT_READONLY` enables the existing explicit `/v2` routing.
- `NIRA_V2_PERSONAL_CONTEXT=ALLOW_READONLY_CONTEXT` enables sending up to 12,000 characters
  of the **existing** `nira_personality.yaml` profile sections plus at most 4,400 characters
  of character, recent conversation and up to four ranked memory candidates to selected cloud model provider.

Without the second gate, V2's behavior and privacy envelope remain as Phase 4A.
`NIRA_V2_PERSONAL_CONTEXT` is a *separate permission*, not implied by `/v2`.

The bridge reads the authoritative `Prompt/nira_personality.yaml` copied to the WPF
output by the legacy project's build. Missing/invalid identity fails closed.
It reads recent turns using `ConversationManager.BuildContextSnapshot` and excludes the
current submitted turn (already added by `NIRAMindRuntime`). It performs local memory
candidate retrieval using `NIRALongTermMemoryService.SearchCandidatesAsync` without
reinforcement or memory formation. Older user messages and retrieved memories are
**untrusted data**, not executable system instructions.

## Existing ownership and safety

- The original NIRA executive still owns ordinary turns, autonomous tasks and ELVARA.
- The V2 general runtime remains read-only; no shell, filesystem mutation, desktop
  control, credential broker or unsupervised memory modification is enabled.
- V2 has no second conversation/memory database; the host writes each completed V2
  answer exactly once to the original conversation archive (Phase 4A behavior).
- Context is captured once per canary run and provided to the same model-driven
  capability registry, not a keyword-routed specialist.
- V2's checkpoints remain DPAPI-protected at rest; **selected personal context is still
  transmitted to selected cloud model provider**, and the actual provider has its own privacy terms.
- Don't share raw model prompts or local diagnostics that may contain personal memory.

## Suggested live checks

1. With both gates set, ask `/v2 What did we discuss immediately before this message?`
   and compare the response with the current main conversation. It should not invent
   older turns.
2. Ask `/v2 What do you remember about our NIRA project?` and confirm the response
   is grounded in actual recalled memory; memory candidate relevance remains a model
   judgment rather than independently fact verified.
3. Ask `/v2 What form do you have on my desktop?` and verify it follows the current
   authoritative NIRA personality without inventing biological embodiment.
4. Unset **only** the personal-context gate, restart NIRA and verify V2 can still
   answer a normal public question without prior conversation or private memory.
5. Check normal legacy chat, ELVARA and prior voice/orb UI again. No other source files
   are replaced by this phase.

## Remaining production work

This release is a conservative identity/continuity integration, **not complete
production parity**. It does not yet transfer legacy memory-formation, goals,
self-model updates, voice expression, visual artifacts or desktop action authority to V2.
A future explicit migration should share *one* host-owned state/memory orchestration,
include per-record consent and provenance, and verify effects after authorized actions.

## Phase 4B identity-boundary correction (November 6, 2026)

The original identity-size test contained a malformed unindented 15,000-character
YAML line. The selector previously ignored that line rather than rejecting it.
`ExtractIdentity` now rejects malformed top-level section headings instead of
silently excluding them, while preserving the 12,000-character limit on selected
identity sections. The existing regression case tests both properly indented
oversized values and malformed unindented values. No new context stores,
permission scopes or memory-write paths are introduced.
