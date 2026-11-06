# NIRA V2 Phase 1D — Native tool protocol and bounded research-answer recovery

## Failure addressed

The Phase 1C live run completed one `web.search` but returned `INVALID_PROVIDER_DECISION`
after the second GPT-OSS call. The exact model text was not captured, so the root cause
cannot be proven from the diagnostic alone. The prior interface required lengthy answer
prose to be escaped inside one JSON string, creating an avoidable fragile boundary.

## Protocol redesign

- Use Ollama `/api/chat`'s documented `tools` field with one registered `web_search`
  function (provider-native name), mapped by the host to the existing `web.search` tool.
- Accept only a **single** native tool call per decision; validate function identity and
  arguments. The model cannot supply vault references, authorization or an executable path.
- Use ordinary assistant `message.content` for cited researched answers: no JSON escaping
  of newlines, quotations or formatting. Citations must match observed source URLs.
- Preserve strictly checked Phase 1C JSON decisions as a backwards-compatibility path.
  Broken JSON is not repaired by regex extraction or turned into a tool action.
- If a reply is malformed, uncited or uses fabricated URLs, request **one** correction
  from the model without repeating completed web searches. Every decision has at most
  two HTTP calls; the agent loop's own decision and tool budgets still apply.
- Do not retry model-originated unknown tool calls, malformed tool arguments, multiple
  native tool calls, or external HTTP/provider failures as a workaround.
- Track model HTTP request count, protocol repair count and selected protocol in the
  research pilot, without printing API keys, raw model bodies or untrusted source text.

## Status and scope

This is a **development reliability release**, not production-ready research. The pilot
still processes **only public, non-sensitive prompts and results**, saves checkpoints
and vault content as **unencrypted JSON under `%TEMP%`**, has no file/desktop actions,
and does not yet verify facts beyond source-URL identity or independently check images.
A citation to a retrieved URL does **not** prove that all factual claims are supported.
The original NIRA/ELVARA files, personality, and memory are not modified.

The native tool schema is supported by Ollama's API. Whether the particular Ollama Cloud
model consistently returns native tool calls must be evaluated with the user's account.
If it does not, the strict Phase 1C legacy JSON decision path remains available.

## Testing

Run the guarded installer with `-AuditOnly` and `-CheckOnly`. It executes the 49 prior
offline checks and 11 additional native-protocol checks and builds ResearchPilot.
On Windows, rerun the public Webb question from Phase 1C with `NIRA_V2_MODEL=gpt-oss:120b`.

Expected: `TOOL_SUCCEEDED web.search`, followed by `RUN_COMPLETED` and `STATUS: Completed`.
Native decisions may be `native-tool` and `plain-text`; existing JSON-compatible model
replies may report `legacy-json`. If correction was required, `PROTOCOL RECOVERIES` may
be 1 for that decision. `MODEL HTTP REQUESTS` can exceed `MODEL DECISIONS` by at most
one per decision due to bounded protocol recovery.

On failure, share the activity/error/status/model-request-count/protocol lines. Do not
share credentials or private sources.
