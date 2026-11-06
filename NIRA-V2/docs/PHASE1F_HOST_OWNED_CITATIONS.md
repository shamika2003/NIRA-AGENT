# NIRA V2 — Phase 1F: host-owned citation IDs

## Why

The public baseline evaluation on November 5, 2026 completed six real searches across three unrelated tasks, but all three ended `RESEARCH_CITATION_REQUIRED` after the one permitted correction. The provider body was deliberately not logged, so the exact output style is unknown. The code demanded exact URL strings from the model; that is too fragile.

## New boundary

- The evidence-context builder assigns deterministic, per-run, **displayed-source** identifiers `[S1]`, `[S2]`, etc. Source order comes from the durable search-result vault; the model never chooses link destinations.
- The prompt asks for these IDs immediately after factual claims. The finalizer resolves each model-written valid ID to the corresponding observed HTTPS URL. Existing exact URL citations remain supported.
- An ID absent from the presented evidence window is rejected (`UNOBSERVED_CITATION_ID`). An unknown URL is still rejected (`UNOBSERVED_SOURCE_URL`). Uncited text remains incomplete. **No automatic citations are appended.**
- One bounded correction attempt is preserved; it does not repeat completed searches. This is not an invitation for unlimited provider calls.
- Only numerical citation diagnostics are printed (candidate length, count of citation IDs, count of URL mentions). Prompts, model responses, API keys and retrieved excerpts are not dumped by the pilot.
- No change to tool authority, cloud endpoint, credential handling, search-vault storage, memory, character, WPF or ELVARA bridge.

## Critical distinction

The citation binder proves **source identity** only: `[S2]` points to the actual second presented source. It does **not** prove the source text entails the adjacent claim, that the source is reliable, or that a negative claim can be inferred from absence of hits. Phase 2 must add full-document retrieval and claim-level evidence verification. The existence of a source ID is never a substitute for fact-checking.

## Test gates

The patch includes 12 new HTTP-stub tests for ID creation, deterministic binding, unknown/unpresented IDs, invented URLs, no sources, uncited prose, recovery, legacy JSON, direct URLs, and unknown tool rejection. Run all 69 original offline checks plus these 12 (81 total) on Windows .NET 10.

Run the public evaluation script again only *after* the offline tests pass. If a run still fails, share the `CITATION DIAGNOSTIC` line and the error code; no private prompt or credential is required.

## Pilot limitations

This public development pilot still writes unencrypted JSON checkpoints to Temp. It is not a private-document or production-ready research workflow. Do not use it with account information, banking data, passwords or private files. NIRA V2 continues to be independent of the old Executive.
