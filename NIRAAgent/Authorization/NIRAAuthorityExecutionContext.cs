namespace NIRAAgent.Authorization;

// Carries trusted runtime ownership into capability authorization without
// exposing goal/branch/work identity as model-controlled capability arguments.
// AsyncLocal lets nested dynamic-tool/capability work inherit the same task
// boundary while parallel branches keep independent contexts.
public sealed record NIRAAuthorityExecutionContext
{
    public Guid RunId { get; init; }
    public Guid? GoalId { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? WorkId { get; init; }
    public bool UserInitiated { get; init; }
    public string TaskLabel { get; init; } = string.Empty;
    // Trusted, ephemeral user-event text; never populated from cognition,
    // retrieved pages, goal titles, branch reasons or model proposals.
    public string DirectUserRequest { get; init; } = string.Empty;

    public bool HasTaskBoundary => GoalId.HasValue && GoalId.Value != Guid.Empty;

    public static NIRAAuthorityExecutionContext Empty { get; } = new();
}

public sealed class NIRAAuthorityExecutionContextAccessor
{
    private readonly AsyncLocal<NIRAAuthorityExecutionContext?> _current = new();

    public NIRAAuthorityExecutionContext Current =>
        _current.Value ?? NIRAAuthorityExecutionContext.Empty;

    public IDisposable Push(NIRAAuthorityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        NIRAAuthorityExecutionContext? previous = _current.Value;
        _current.Value = context;

        return new RestoreLease(this, previous);
    }

    private sealed class RestoreLease : IDisposable
    {
        private NIRAAuthorityExecutionContextAccessor? _owner;
        private readonly NIRAAuthorityExecutionContext? _previous;

        public RestoreLease(
            NIRAAuthorityExecutionContextAccessor owner,
            NIRAAuthorityExecutionContext? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            NIRAAuthorityExecutionContextAccessor? owner =
                Interlocked.Exchange(ref _owner, null);

            if (owner != null)
            {
                owner._current.Value = _previous;
            }
        }
    }
}

