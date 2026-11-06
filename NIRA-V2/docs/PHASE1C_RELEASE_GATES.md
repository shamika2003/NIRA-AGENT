# Phase 1C release gates

- [ ] All previous Phase 0/1A/1B checks pass (38 known tests)
- [ ] New research-loop offline checks pass (11 tests)
- [ ] Nira.V2.ResearchPilot builds in .NET 10 without warnings as errors
- [ ] Client does not send cloud/Tavily key in POST JSON, vault or checkpoints
- [ ] Model cannot choose opaque argument/result references
- [ ] Unknown/unsafe tool identities are rejected, no HTTP requested
- [ ] Research answer without an observed URL rejected
- [ ] Citation URL must match observed source URI
- [ ] Source duplicates, invalid schemes and IP literal URLs filtered
- [ ] Restarted read-only search can retrieve prior scoped input
- [ ] Real Ollama Cloud + Tavily search yields one tool dispatch and a cited answer
- [ ] Real failure paths (no key / 401 / 429 / timeout / empty results) checked

Passing offline tests is not proof of research quality, prompt injection resistance,
proper fact-checking, or production security. Do not enable computer-control tools here.
