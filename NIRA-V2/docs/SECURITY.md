# NIRA V2 foundation security notes

This is an independent development foundation, not yet a security-reviewed production runtime.

- `FileAgentStateStore` writes run metadata and user requests as plain local JSON to its explicitly selected folder. **Do not use it for real private user conversations or secrets** before encrypted production persistence is implemented.
- Tool arguments in run checkpoints are `ToolArgumentReference` IDs, **not raw credentials or arbitrarily executable strings**. Real adapters need a separately permissioned encrypted input vault.
- Models cannot grant permissions, register tools or execute side effects without the host capability gate.
- `OutcomeConfirmed` must only be true after independent verification of a state change; do not blindly trust provider response status.
- An in-flight state-changing operation on restart is `NeedsReconciliation`, even when it has an idempotency key.
- The offline demos and tests only use synthetic fixtures, no model credentials, web access or user state.
- File lease prevents simultaneous same-run execution while the process is alive; transactional persistence, multi-process persistence versioning and crash-consistency stress testing remain required.
- Progress events contain status codes and tool IDs but no chain-of-thought, raw source content or secret tool parameters.
