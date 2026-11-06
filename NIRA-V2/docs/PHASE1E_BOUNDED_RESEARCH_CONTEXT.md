# NIRA V2 Phase 1E — Bounded research context and evidence-aware synthesis

## Observed failure

The live Phase 1D research session completed four Tavily searches but did not synthesize.
`OllamaResearchReasoner` concatenated all raw search-result JSON until the 16,000-char
cap failed before the fifth model request (`RESEARCH_CONTEXT_TOO_LARGE`). The search
results were durable; their presentation to the model wasn't budgeted.

## Changes (development pilot only)

- The **evidence vault is unchanged**. Search result records are retained even when
  their brief excerpts are omitted from the LLM context.
- `ResearchEvidenceContext` constructs a deterministic <=12,000-char VIEW, with
  deduplicated HTTPS source URLs, short titles, bounded excerpts, and prior query names.
  Full source URLs remain in the observed-citation allowlist. Source text is untrusted.
- The **host policy**, not an LLM instruction alone, switches to final synthesis
  after two productive searches with >=3 distinct observed URLs, or after four
  search calls with any usable evidence. It disables the `tools` field for that turn.
- If a model still proposes a tool after synthesis is required, it is not executed.
  One bounded protocol correction is allowed. A repeated search query is also refused.
- Existing exact observed-URL citation validation and unknown-tool rejection are unchanged.
- Safe, numeric metrics are displayed: raw/compacted character counts, distinct/duplicate
  sources, and whether synthesis was required. No source body or model prompt is dumped.
- Offline tests simulate four large search responses, duplicate queries, the synthesis
  boundary, fabricated citations, and insufficient evidence.

**These budgets describe a narrow public ResearchPilot, not deep-research production
policy.** Deep research needs explicit task coverage and evidence adequacy measures,
full-page retrieval, claim-level verification and a versioned encrypted store. Do not
claim those capabilities are finished just because this context fix passes.

## Diagnostics

From `NIRA-V2`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Inspect-ResearchPilotRun.ps1
```

To inspect a particular run, add `-CheckpointId <GUID>` after the script path.
The script prints only status, counters and activity codes. Do not share raw JSON
checkpoints or vault entries; they contain unencrypted prompts and retrieved text.

## Live check

Run the public Webb test again. Expect a complete cited answer and contextual telemetry.
The host should now request synthesis after useful evidence, not exhaust all four
search calls. All Phase 0–1D offline suites and the nine Phase 1E tests must pass.

Existing NIRAAgent, UI, memory, credentials, voice and ELVARA bridge are untouched.
