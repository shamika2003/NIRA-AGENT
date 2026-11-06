# Phase 2A — Evidence identity, review records and publication gate

**Status:** Independent, add-only engineering foundation; NOT a production fact checker or a replacement for the current Phase 1F ResearchPilot.

## The regression that motivates this work

On 2026-11-05, Phase 1F passed 81 offline checks and completed all three unrelated live research tasks. A manual answer inspection still identified possible false statements about heat-pump refrigeration components, and the Mars answer quoted Wikipedia and YouTube even though NASA/JPL evidence was requested. The captured evaluation output also contains Unicode mojibake. Citation identity validation alone cannot establish factual support.

## Implemented

- `EvidenceLedger` is an in-memory domain object per run; it records host-issued source identity, exact retrieved material, SHA-256 content digest, retrieval timestamp and tool-observation reference. It does **not** retrieve a page or independently authenticate a publisher.
- A research requirement can specify allowed source host domains; domain matching is case insensitive and respects whole-domain labels (`science.nasa.gov` matches `nasa.gov`, `evilnasa.gov` does not).
- `ClaimEntry` binds a bounded statement to observed source IDs, initially without any favorable review. Absence of a review = not publication eligible.
- `IndependentReview` records reviewer ID, outcome, rationale and exact quote anchors (source ID, start offset, length and literal quote). The reviewer must be different from the stated drafter.
- The ledger rejects favorable assessments based only on search snippets, unknown sources, off-scope domains or invented quotation anchors. A qualified assessment must retain its qualification.
- `EvaluatePublication` returns an explicit allow/deny status for each claim. Unsupported and disputed claims cannot pass this gate.
- The offline `EvidenceAudit` app reads the existing PUBLIC evaluation ZIP, reports off-scope citation hosts and suspicious encoding sequences, without accessing secrets, raw model requests or the private V2 checkpoint vault.

## Crucial limitations

**A matching quotation is not a semantic entailment test.** A separate real claim reviewer must evaluate whether the passage actually supports the claim, including numbers, dates, negations, conflict and domain constraints; independent review records are not automatic truth certificates.

Likewise, a URL on an official domain is not proof the page was fetched from that authority or that its quoted content is accurate. Search-result excerpts remain `SearchExcerpt` and are not promotable by this gate. Phase 2B will need a controlled full-document retriever, content integrity and provenance, claim decomposition, actual verifier, contradiction tracking, targeted repairs and adversarial testing. A writer/LLM cannot authorize itself to publish unreviewed claims. Before connecting private data, add encryption, persistence versioning, retention controls and an explicit trust boundary for reviewer credentials.

This foundation changes no Phase 0-1F files, original NIRA source, WPF UI, memory, credentials or ELVARA bridge.

## Windows verification

Run `Apply-V2-Evidence.ps1 -ProjectRoot <path> -AuditOnly` followed by `-CheckOnly` from the extracted ZIP. `CheckOnly` installs only new files then builds/runs all existing 81 checks plus the new Evidence tests. Existing source is never overwritten.

To audit the public logs (not private checkpoint JSON):

```powershell
Set-Location "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT\NIRA-V2"
dotnet run --project ".\apps\Nira.V2.EvidenceAudit" -- "$env:TEMP\NIRA-V2-Evaluations\phase1e-20261105-143854-public-logs.zip"
```

To move toward Phase 2B, we must join this ledger to an actual controlled document fetcher and separate reviewer before allowing its output into the public ResearchPilot response.
