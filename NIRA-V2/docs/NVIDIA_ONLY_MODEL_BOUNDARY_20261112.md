# NIRA model-provider boundary — NVIDIA active

## Owner / continuity rules

NIRA's identity, character, conversation and long-term-memory owners, encrypted V2
checkpoint and payload vault, goal/branch SQLite state, authorizations, approval
toasts, capability registry, tool receipts, citation checks and Phase 4H.6 behavior
remain authoritative in the same local host. A model provider is not a new agent,
not a second memory store and cannot execute or authorize a tool.

## Model architecture

- Active V2 model adapter: `Nira.V2.ModelProvider`. Its selection, HTTPS endpoint,
  process credential source and provider-specific request options are confined to
  the adapter module. `GeneralModelReasoner` owns the normalized decision flow and
  delegates tool validation and execution to existing NIRA host services.
- Active legacy-compatibility adapter: `NIRAAgent/AI/Models/NIRAActiveModelProvider.cs`.
  Older cognition, revision, memory-formation and optional vision call sites use
  `NIRAModelClient`, but do not hardcode an API URL. During V2 migration both
  adapters target the same NVIDIA Chat Completions API.
- Active model: `nvidia/nemotron-3.5-lightning-30b-a3b` by default. You may override
  the V2 model with `-Model` at launch, without changing host identity/permissions.
  Legacy role-specific overrides use `NIRA_MODEL_PRIMARY`, `NIRA_MODEL_DEEP`,
  `NIRA_MODEL_VISION`, `NIRA_MODEL_RESPONSE`, and
  `NIRA_MODEL_COMPLETION_REVIEW` (when those consumers opt in).
- Credential: `NIRA_NVIDIA_API_KEY` (process only), with `NVIDIA_API_KEY` as a
  process-only fallback. Do not add keys to repository files or transcript logs.

## Speech, vision and media

Voice synthesis is independent of the reasoning API. The local Piper speech
engine remains available under NIRA's existing volume/enable controls. A former
remote voice option is retired, and old persisted numeric mode 1 is safely
normalized to local Piper. Do not delete saved voice preferences or other user data.

The legacy vision client now encodes images as OpenAI-style data URLs with
bounded sizes and MIME types; **the selected NVIDIA model must support image
inputs**. The default text-oriented model is not a guarantee of vision support.
Unsupported vision requests must fail transparently; they must never claim to
have observed an image. Image discovery and Cloudflare image generation remain
separate services and retain their original authorization checks.

## Safety and migration retention

No deletion of local `%LOCALAPPDATA%` NIRA state, user prompts, memories, SQLite
files, authorization grants, DPAPI secrets, or Git history is authorized by this
provider cleanup. Historic source/doc snapshots and retired protocol tests are
archived outside the active checkout by the guarded installer, not destroyed.

The installer performs per-file hash preflight, permits safe resumption after an
interruption, restores pre-install source on a failed build/test, and keeps a
recovery snapshot. Offline tests are necessary but not a substitute for live
acceptance of a simple chat response, one read-only capability, and one approval
boundary in the actual NVIDIA session.

## Resume checkpoint

Continue NIRA V2 migration from the preexisting Phase 4H.6 owner-approved
branch-completion milestone. Do not restart V2 or recreate identity/state.
