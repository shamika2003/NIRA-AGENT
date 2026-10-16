using System.Runtime.CompilerServices;

// Grants the dedicated offline regression assembly access to internal
// implementation details without exposing them through NIRAAgent's public API.
// This has no effect on capability execution, permissions, or model behavior.
[assembly: InternalsVisibleTo("NIRAAgent.RegressionChecks")]
