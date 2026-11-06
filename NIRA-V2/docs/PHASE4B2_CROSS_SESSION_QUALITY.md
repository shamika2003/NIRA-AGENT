# Phase 4B.2 — Cross-session continuity and memory relevance

**One NIRA:** Existing WPF, conversation archive, personality YAML, character and
long-term memory stores remain authoritative. No new identity/database is created.

When an opted-in new WPF session lacks prior live turns, the existing archive
supplies limited earlier-session context from its bounded 21-day window.
Context identifies LIVE vs ARCHIVED turns separately. A missing historical
excerpt does not prove earlier conversations never occurred.

Long-term memory candidates now need a topical retrieval signal, not merely
memory importance/recency. Last-update date and retrieval score accompany
selected memory excerpts. These excerpts are fallible recollections, not
verified facts about today's files, preferences, or application state.

Both `NIRA_V2_WPF_CANARY=EXPLICIT_READONLY` and
`NIRA_V2_PERSONAL_CONTEXT=ALLOW_READONLY_CONTEXT` are still required to
send the context to Ollama Cloud. No filesystem reads, writes or desktop
control are introduced. The existing archive is read without copying it into
another memory store. The existing archive provider may perform its normal
lazy database/session initialization.

Release gates: prior 173 plus 11 new offline checks, successful actual WPF and
legacy builds, then a live `/v2` prior-chat test. The archive's existing 21-day
window is not exhaustive historical search. Relevance thresholds are
conservative heuristics, not a full memory veracity checker.
