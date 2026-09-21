using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NIRAAgent.Goals;

using NIRAAgent.Capabilities;

namespace NIRAAgent.Authorization;

public sealed class NIRAScopedCapabilityAuthorizer : INIRACapabilityAuthorizer
{
    private static readonly string[] WorkspaceCapabilityIds =
    [
        NIRACapabilityIds.FileWrite,
        NIRACapabilityIds.FileCopy,
        NIRACapabilityIds.FileMove,
        NIRACapabilityIds.DirectoryCreate
    ];

    private static readonly string[] ProjectExecutionCapabilityIds =
    [
        NIRACapabilityIds.ShellExecute,
        NIRACapabilityIds.ProcessStart
    ];

    private static readonly string[] BrowserSiteCapabilityIds =
    [
        NIRACapabilityIds.BrowserClick,
        NIRACapabilityIds.BrowserFill,
        NIRACapabilityIds.BrowserSelect,
        NIRACapabilityIds.BrowserDownload,
        NIRACapabilityIds.BrowserAuthenticate
    ];

    private readonly NIRAAuthorityStore _store;
    private readonly NIRACapabilityRequestPolicy _policy;
    private readonly NIRACapabilityApprovalBroker _broker;
    private readonly NIRAAuthorityExecutionContextAccessor _executionContext;
    private readonly NIRAGoalService _goals;

    // Suppress repeated requests for the same declined action during this
    // work. A NEW explicit user turn may request approval again. Not durable:
    // denial does not silently become an indefinite global permission rule.
    private sealed record RecentDenial(Guid RunId, DateTimeOffset AtUtc);
    private readonly ConcurrentDictionary<string, RecentDenial> _recentDenials = new();

    public NIRAScopedCapabilityAuthorizer(
        NIRAAuthorityStore store,
        NIRACapabilityRequestPolicy policy,
        NIRACapabilityApprovalBroker broker,
        NIRAAuthorityExecutionContextAccessor executionContext,
        NIRAGoalService goals)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
    }

    public async Task<NIRACapabilityAuthorizationDecision> AuthorizeAsync(
        NIRACapabilityDescriptor descriptor,
        NIRACapabilityRisk risk,
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NIRAAuthorityOperation operation = DescribeWithContext(request, risk);
        IReadOnlyList<NIRAAuthorityScope> scopes = _store.ReadScopes();

        if (risk == NIRACapabilityRisk.Observe &&
            !operation.Sensitive &&
            !operation.RequiresExplicitAuthorization)
        {
            return Allow(operation, "Read-only baseline permission.") with
            {
                AuthorityBasis = "BaselineObserve"
            };
        }

        if (NIRARiskAdaptiveAuthority.IsSafeLocalObservation(operation))
        {
            return Allow(operation, "Allowlisted local read-only clock observation.") with
            {
                AuthorityBasis = NIRARiskAdaptiveAuthority.BaselineCommandBasis
            };
        }

        NIRAAuthorityScope? match =
            scopes.FirstOrDefault(scope =>
                Matches(scope, operation) && IsScopeOwnerOpen(scope));

        if (match != null)
        {
            Debug.WriteLine(
                $"[Authorization] ALLOWED | Id={descriptor.Id} | Scope={match.Id:D} | Kind={match.Kind}");

            return Allow(
                operation,
                match.Kind == NIRAAuthorityScopeKind.Task
                    ? "Matched authority already delegated for this task."
                    : "Matched a persistent permission scope.",
                match.Id);
        }

        // Direct user-event authority is deliberately narrow: only an
        // explicitly named NEW file or exact dotnet clean/restore/build/test
        // in the exact mentioned project. No remembered grant is created.
        if (NIRARiskAdaptiveAuthority.IsDirectUserDelegatedAction(
                operation, _policy, _store.ProtectedDirectory))
        {
            return Allow(operation, "Exact bounded action explicitly requested by the user in this run.") with
            {
                AuthorityBasis = NIRARiskAdaptiveAuthority.DirectTaskBasis,
                DerivedFromDirectUserRequest = true
            };
        }

        // A denial is a result, not an invitation to re-open the same prompt
        // on the following cognition cycle or background branch wake.
        string denialKey = BuildDenialKey(operation);
        if (_recentDenials.TryGetValue(denialKey, out RecentDenial? denial))
        {
            bool explicitlyRetriedInNewTurn =
                operation.ExecutionContext.UserInitiated &&
                operation.ExecutionContext.RunId != denial.RunId;
            if (explicitlyRetriedInNewTurn ||
                DateTimeOffset.UtcNow - denial.AtUtc > TimeSpan.FromMinutes(30))
                _recentDenials.TryRemove(denialKey, out _);
            else
                return NIRACapabilityAuthorizationDecision.Deny(
                    "This exact action was declined during this task. " +
                    "Do not request it again unless the user explicitly asks anew.");
        }

        Debug.WriteLine(
            $"[Authorization] WAITING | Id={descriptor.Id} | Risk={risk} | " +
            $"Goal={operation.ExecutionContext.GoalId?.ToString("D") ?? "-"}");

        NIRAApprovalResponse response =
            await _broker.RequestAsync(operation, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (response.Choice == NIRAApprovalChoice.Deny)
        {
            _recentDenials[denialKey] = new RecentDenial(
                operation.ExecutionContext.RunId, DateTimeOffset.UtcNow);
            return NIRACapabilityAuthorizationDecision.Deny(
                string.IsNullOrWhiteSpace(response.Reason)
                    ? "The user declined this request. No action was started."
                    : response.Reason);
        }

        // Repeat canonical path/origin/identity checks after the user has had
        // time to review. Also refresh the trusted execution context.
        operation = DescribeWithContext(request, risk);

        // The user may have cancelled/completed the parent goal while the
        // permission dialog was open. It must never create a new task grant
        // or dispatch stale branch work after that terminal transition.
        if (operation.ExecutionContext.GoalId is Guid pendingGoal &&
            pendingGoal != Guid.Empty && !IsGoalOpen(pendingGoal))
            return NIRACapabilityAuthorizationDecision.Deny(
                "The owning task finished or was cancelled while permission was pending.");

        _recentDenials.TryRemove(denialKey, out _);

        if (response.Choice == NIRAApprovalChoice.AllowOnce)
        {
            return Allow(
                operation,
                "The user approved this exact request once.");
        }

        if (response.Choice == NIRAApprovalChoice.AllowTask)
        {
            NIRAAuthorityScope taskScope =
                BuildTaskScope(operation, response);

            _store.Grant(taskScope);

            return Allow(
                operation,
                "The user delegated matching authority for this persistent task.",
                taskScope.Id);
        }

        if (response.Choice != NIRAApprovalChoice.Remember)
        {
            return NIRACapabilityAuthorizationDecision.Deny(
                "Unknown approval choice.");
        }

        NIRAAuthorityScope scope =
            BuildRememberedScope(operation, response);

        _store.Grant(scope);

        return Allow(
            operation,
            "The user created a persistent permission scope.",
            scope.Id);
    }

    public Task<NIRACapabilityAuthorizationDecision> ValidateForDispatchAsync(
        NIRACapabilityDescriptor descriptor,
        NIRACapabilityRisk risk,
        NIRACapabilityRequest request,
        NIRACapabilityAuthorizationDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NIRAAuthorityOperation operation = DescribeWithContext(request, risk);

        // Applies to one-time approvals too: a task that resolves while an
        // approval is pending must not dispatch an orphaned side effect.
        if (operation.ExecutionContext.GoalId is Guid owner &&
            owner != Guid.Empty && !IsGoalOpen(owner))
            return Task.FromResult(
                NIRACapabilityAuthorizationDecision.Deny(
                    "The owning task has completed or was cancelled."));

        if (decision.Status != NIRACapabilityAuthorizationStatus.Allowed ||
            decision.RequestFingerprint != operation.Fingerprint)
        {
            return Task.FromResult(
                NIRACapabilityAuthorizationDecision.Deny(
                    "The approved request does not match the request to dispatch."));
        }

        if (decision.DerivedFromDirectUserRequest &&
            (decision.AuthorityBasis != NIRARiskAdaptiveAuthority.DirectTaskBasis ||
             !NIRARiskAdaptiveAuthority.IsDirectUserDelegatedAction(
                 operation, _policy, _store.ProtectedDirectory)))
        {
            return Task.FromResult(
                NIRACapabilityAuthorizationDecision.RequireAuthorization(
                    "The original direct-user boundary no longer covers this request."));
        }

        if (decision.ScopeId.HasValue &&
            !_store.ReadScopes().Any(
                scope =>
                    scope.Id == decision.ScopeId &&
                    Matches(scope, operation) &&
                    IsScopeOwnerOpen(scope)))
        {
            return Task.FromResult(
                NIRACapabilityAuthorizationDecision.RequireAuthorization(
                    "The permission was revoked, expired, or no longer matches the current task boundary before dispatch."));
        }

        return Task.FromResult(decision);
    }

    private bool IsGoalOpen(Guid goalId) =>
        _goals.TryGetGoal(goalId, out NIRAGoalState? goal) &&
        goal is { IsOpen: true };

    private bool IsScopeOwnerOpen(NIRAAuthorityScope scope) =>
        scope.Kind != NIRAAuthorityScopeKind.Task ||
        (scope.GoalId.HasValue && IsGoalOpen(scope.GoalId.Value));

    private static string BuildDenialKey(NIRAAuthorityOperation operation)
    {
        NIRAAuthorityExecutionContext context = operation.ExecutionContext;
        Guid boundary = context.GoalId is Guid goal && goal != Guid.Empty
            ? goal : context.RunId;
        return $"{boundary:D}:{operation.Fingerprint}";
    }

    private NIRAAuthorityOperation DescribeWithContext(
        NIRACapabilityRequest request,
        NIRACapabilityRisk risk)
    {
        return _policy.Describe(request, risk) with
        {
            ExecutionContext = _executionContext.Current
        };
    }

    private static NIRACapabilityAuthorizationDecision Allow(
        NIRAAuthorityOperation operation,
        string reason,
        Guid? scopeId = null) =>
        NIRACapabilityAuthorizationDecision.Allow(reason) with
        {
            ScopeId = scopeId,
            AuthorityBasis = scopeId.HasValue ? "ScopedGrant" : "AllowOnce",
            RequestFingerprint = operation.Fingerprint
        };

    public NIRAAuthorityScope BuildRememberedScope(
        NIRAAuthorityOperation operation,
        NIRAApprovalResponse response)
    {
        if (!operation.CanRemember)
        {
            throw new InvalidOperationException(
                "This request requires approval each time.");
        }

        NIRAAuthorityScope scope = new()
        {
            Label = string.IsNullOrWhiteSpace(response.Label)
                ? operation.Request.CapabilityId
                : response.Label.Trim(),
            CapabilityIds = [operation.Request.CapabilityId],
            AllowDestructive =
                response.AllowDestructive ||
                operation.Risk == NIRACapabilityRisk.Destructive,
            AllowCredentialUse = operation.RequiresCredentialUse
        };

        IReadOnlyList<string> executionProfiles =
            NIRAAuthorityExecutionProfiles.NormalizeIds(
                response.ExecutionProfileIds);

        if (operation.Paths.Length > 0 &&
            (operation.Request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
             operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload))
        {
            string root = ResolveGrantRoot(operation, response);

            List<string> ids = [.. WorkspaceCapabilityIds];

            if (operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload)
            {
                ids.Add(NIRACapabilityIds.HttpDownload);
            }

            if (scope.AllowDestructive)
            {
                ids.Add(NIRACapabilityIds.FileDelete);
            }

            if (executionProfiles.Count > 0)
            {
                ids.AddRange(ProjectExecutionCapabilityIds);
            }

            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.Folder,
                RootPath = root,
                Origin = operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload
                    ? operation.Origin
                    : string.Empty,
                CapabilityIds = ids.Distinct(StringComparer.Ordinal).ToArray(),
                ExecutionProfileIds = executionProfiles.ToArray()
            };
        }
        else if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                     operation.Request.CapabilityId) &&
                 executionProfiles.Count > 0 &&
                 !string.IsNullOrWhiteSpace(operation.WorkingDirectory) &&
                 NIRAAuthorityExecutionProfiles.MatchesAny(
                     operation,
                     executionProfiles,
                     out _))
        {
            string root =
                ResolveExecutionGrantRoot(
                    operation,
                    response);

            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.Folder,
                RootPath = root,
                CapabilityIds = ProjectExecutionCapabilityIds,
                ExecutionProfileIds = executionProfiles.ToArray()
            };
        }
        else if (operation.Request.CapabilityId == NIRACapabilityIds.HttpRequest)
        {
            scope = operation.Risk == NIRACapabilityRisk.Observe
                ? scope with
                {
                    Kind = NIRAAuthorityScopeKind.HttpOrigin,
                    Origin = operation.Origin,
                    Method = operation.Method
                }
                : scope with
                {
                    Kind = NIRAAuthorityScopeKind.ExactRequest,
                    Fingerprint = operation.Fingerprint
                };
        }
        else if (operation.Request.CapabilityId == NIRACapabilityIds.BrowserRequest &&
                 operation.Risk != NIRACapabilityRisk.Observe)
        {
            // Authenticated state-changing browser-context API calls remain exact.
            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.ExactRequest,
                Fingerprint = operation.Fingerprint
            };
        }
        else if (NIRACapabilityRequestPolicy.IsBrowserOriginScoped(operation.Request.CapabilityId))
        {
            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.BrowserOrigin,
                Origin = operation.Origin,
                CapabilityIds = BrowserSiteCapabilityIds,
                AllowCredentialUse = operation.RequiresCredentialUse
            };
        }
        else if (operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect)
        {
            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.VisionCloud
            };
        }
        else
        {
            scope = scope with
            {
                Kind = NIRAAuthorityScopeKind.ExactRequest,
                Fingerprint = operation.Fingerprint
            };
        }

        if (!Matches(scope, operation))
        {
            throw new InvalidOperationException(
                "The remembered permission scope does not cover this request.");
        }

        return scope;
    }

    public NIRAAuthorityScope BuildTaskScope(
        NIRAAuthorityOperation operation,
        NIRAApprovalResponse response)
    {
        if (!operation.CanRemember)
        {
            throw new InvalidOperationException(
                "This request cannot be delegated beyond a single approval.");
        }

        if (!operation.ExecutionContext.HasTaskBoundary ||
            !operation.ExecutionContext.GoalId.HasValue)
        {
            throw new InvalidOperationException(
                "Task delegation requires an authoritative persistent goal boundary.");
        }

        Guid goalId = operation.ExecutionContext.GoalId.Value;
        bool allowDestructive =
            response.AllowDestructive ||
            operation.Risk == NIRACapabilityRisk.Destructive;

        IReadOnlyList<string> executionProfiles =
            NIRAAuthorityExecutionProfiles.NormalizeIds(
                response.ExecutionProfileIds);

        if (!IsGoalOpen(goalId))
            throw new InvalidOperationException(
                "Cannot delegate authority to a completed or cancelled task.");

        NIRAAuthorityScope scope = new()
        {
            Kind = NIRAAuthorityScopeKind.Task,
            GoalId = goalId,
            Label = string.IsNullOrWhiteSpace(operation.ExecutionContext.TaskLabel)
                ? $"Task {goalId:D}"
                : operation.ExecutionContext.TaskLabel.Trim(),
            CapabilityIds = [operation.Request.CapabilityId],
            AllowDestructive = allowDestructive,
            AllowCredentialUse = operation.RequiresCredentialUse
        };

        if (operation.Paths.Length > 0 &&
            (operation.Request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
             operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload))
        {
            string root = ResolveGrantRoot(operation, response);

            List<string> ids = [.. WorkspaceCapabilityIds];
            if (operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload)
            {
                ids.Add(NIRACapabilityIds.HttpDownload);
            }
            if (allowDestructive)
            {
                ids.Add(NIRACapabilityIds.FileDelete);
            }

            if (executionProfiles.Count > 0)
            {
                ids.AddRange(ProjectExecutionCapabilityIds);
            }

            scope = scope with
            {
                RootPath = root,
                Origin = operation.Request.CapabilityId == NIRACapabilityIds.HttpDownload
                    ? operation.Origin
                    : string.Empty,
                CapabilityIds = ids.Distinct(StringComparer.Ordinal).ToArray(),
                ExecutionProfileIds = executionProfiles.ToArray()
            };
        }
        else if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                     operation.Request.CapabilityId) &&
                 executionProfiles.Count > 0 &&
                 !string.IsNullOrWhiteSpace(operation.WorkingDirectory) &&
                 NIRAAuthorityExecutionProfiles.MatchesAny(
                     operation,
                     executionProfiles,
                     out _))
        {
            scope = scope with
            {
                RootPath = ResolveExecutionGrantRoot(operation, response),
                CapabilityIds = ProjectExecutionCapabilityIds,
                ExecutionProfileIds = executionProfiles.ToArray()
            };
        }
        else if (NIRACapabilityRequestPolicy.IsBrowserOriginScoped(operation.Request.CapabilityId))
        {
            scope = scope with
            {
                Origin = operation.Origin,
                CapabilityIds = BrowserSiteCapabilityIds,
                AllowCredentialUse = operation.RequiresCredentialUse
            };
        }
        else if (operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect)
        {
            scope = scope with
            {
                CapabilityIds = [NIRACapabilityIds.VisionInspect]
            };
        }
        else
        {
            // Commands outside an explicitly selected bounded execution profile,
            // process control, and consequential API writes stay exact even inside
            // a task. Project trust never becomes unrestricted shell authority.
            scope = scope with
            {
                Fingerprint = operation.Fingerprint
            };
        }

        if (!Matches(scope, operation))
        {
            throw new InvalidOperationException(
                "The task permission does not cover this request.");
        }

        return scope;
    }

    public NIRAAuthorityScope GrantFolder(
        string label,
        string root,
        bool allowDownloads,
        bool allowDestructive,
        IEnumerable<string>? executionProfileIds = null)
    {
        root = NIRACapabilityRequestPolicy.NormalizeLocalPath(root);
        _policy.ValidatePath(root);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "Choose an existing project folder.");
        }

        if (NIRACapabilityRequestPolicy.IsWithin(root, _store.ProtectedDirectory))
        {
            throw new InvalidOperationException(
                "The authority directory cannot be granted.");
        }

        IReadOnlyList<string> executionProfiles =
            NIRAAuthorityExecutionProfiles.NormalizeIds(
                executionProfileIds);

        List<string> ids = [.. WorkspaceCapabilityIds];
        if (allowDownloads)
        {
            ids.Add(NIRACapabilityIds.HttpDownload);
        }
        if (allowDestructive)
        {
            ids.Add(NIRACapabilityIds.FileDelete);
        }

        if (executionProfiles.Count > 0)
        {
            ids.AddRange(ProjectExecutionCapabilityIds);
        }

        NIRAAuthorityScope scope = new()
        {
            Kind = NIRAAuthorityScopeKind.Folder,
            RootPath = root,
            CapabilityIds = ids.Distinct(StringComparer.Ordinal).ToArray(),
            ExecutionProfileIds = executionProfiles.ToArray(),
            AllowDestructive = allowDestructive,
            Label = string.IsNullOrWhiteSpace(label)
                ? Path.GetFileName(root)
                : label.Trim()
        };

        _store.Grant(scope);
        return scope;
    }

    public static bool Matches(
        NIRAAuthorityScope scope,
        NIRAAuthorityOperation operation)
    {
        if (!scope.Active ||
            operation.Sensitive ||
            !operation.CanRemember ||
            !scope.CapabilityIds.Contains(
                operation.Request.CapabilityId,
                StringComparer.Ordinal) ||
            (operation.Risk == NIRACapabilityRisk.Destructive && !scope.AllowDestructive) ||
            (operation.RequiresCredentialUse && !scope.AllowCredentialUse))
        {
            return false;
        }

        if (scope.Kind == NIRAAuthorityScopeKind.Task)
        {
            if (!scope.GoalId.HasValue ||
                !operation.ExecutionContext.GoalId.HasValue ||
                scope.GoalId.Value != operation.ExecutionContext.GoalId.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(scope.Fingerprint) &&
                !string.Equals(
                    scope.Fingerprint,
                    operation.Fingerprint,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(scope.RootPath))
            {
                if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                        operation.Request.CapabilityId))
                {
                    if (string.IsNullOrWhiteSpace(operation.WorkingDirectory) ||
                        !NIRACapabilityRequestPolicy.IsWithin(
                            operation.WorkingDirectory,
                            scope.RootPath) ||
                        !NIRAAuthorityExecutionProfiles.MatchesAny(
                            operation,
                            scope.ExecutionProfileIds,
                            out _))
                    {
                        return false;
                    }
                }
                else if (operation.Paths.Length == 0 ||
                         !operation.Paths.All(
                             path =>
                                 NIRACapabilityRequestPolicy.IsWithin(path, scope.RootPath)))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(scope.Origin) &&
                !string.Equals(
                    scope.Origin,
                    operation.Origin,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        return scope.Kind switch
        {
            NIRAAuthorityScopeKind.Folder =>
                MatchesFolderScope(scope, operation),

            NIRAAuthorityScopeKind.HttpOrigin =>
                operation.Request.CapabilityId == NIRACapabilityIds.HttpRequest &&
                scope.Origin.Length > 0 &&
                string.Equals(
                    scope.Origin,
                    operation.Origin,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    scope.Method,
                    operation.Method,
                    StringComparison.OrdinalIgnoreCase),

            NIRAAuthorityScopeKind.BrowserOrigin =>
                NIRACapabilityRequestPolicy.IsBrowserOriginScoped(operation.Request.CapabilityId) &&
                scope.Origin.Length > 0 &&
                string.Equals(
                    scope.Origin,
                    operation.Origin,
                    StringComparison.OrdinalIgnoreCase),

            NIRAAuthorityScopeKind.VisionCloud =>
                operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect,

            NIRAAuthorityScopeKind.ExactRequest =>
                scope.Fingerprint.Length > 0 &&
                string.Equals(
                    scope.Fingerprint,
                    operation.Fingerprint,
                    StringComparison.Ordinal),

            _ => false
        };
    }

    public string BuildCognitionContext()
    {
        StringBuilder text = new();
        text.AppendLine("CURRENT AUTHORITATIVE PERMISSIONS");
        text.AppendLine("NIRA uses low-friction delegated authority. The runtime still owns authorization; model proposals and chat memories do not directly grant OS access.");
        text.AppendLine("Ordinary local read-only observation uses baseline permission. State-changing work first checks task-bound or persistent scopes. Matching authority is reused silently so NIRA should not repeatedly ask for every file edit, browser click or form fill.");
        text.AppendLine("Stage 14.0C: Exact Get-Date local clock observation is silent. Within the CURRENT direct user turn only, the runtime may silently carry out an explicitly named new file in the stated directory or a bare dotnet clean/restore/build/test in the explicitly stated .NET project. This does not authorize modifying existing files, shell flags, arbitrary commands or future/background goals; use task/project grants for those. The action audit still records the result.");
        text.AppendLine("When a capability needs user review during a persistent goal, the trusted Permissions UI may offer Allow once, Allow for this task, or a persistent remembered scope. Task authority is bound to the exact authoritative goal GUID and cannot be reused by another goal.");
        text.AppendLine("Project/folder grants cover normal direct file creation/write/copy/move operations within that root; destructive deletion is separate unless explicitly included. A project grant may also contain explicit bounded execution profiles such as .NET build/test or read-only Git inspection. These profiles match only recognized command families inside the approved working directory; they never turn the folder into unrestricted shell authority.");
        text.AppendLine("Browser site grants cover normal grounded browser interactions on the approved origin. Secure browser authentication may use a credential only when the scope explicitly permits credential use. Passwords/OTP/PIN/payment secrets/API keys never enter model-visible capability arguments.");
        text.AppendLine("Authenticated state-changing browser-context HTTP requests remain exact-request authorization because they may mutate account/server state outside visible UI semantics.");
        text.AppendLine("External/cloud observations such as vision.inspect require explicit authority because screen pixels leave the PC. Revocation applies to future dispatches and never undoes completed work.");
        text.AppendLine("Never claim success from approval alone. Wait for the actual capability result and verify the requested outcome from evidence.");

        NIRAAuthorityScope[] scopes =
            _store.ReadScopes()
                .Where(scope => scope.Active)
                .ToArray();

        text.AppendLine($"Active authority scopes: {scopes.Length}");
        foreach (NIRAAuthorityScope scope in scopes.Take(50))
        {
            text.AppendLine(
                $"- {scope.Id:D} | {scope.Kind} | {scope.Description}");
        }

        if (scopes.Length > 50)
        {
            text.AppendLine(
                "Additional scopes are omitted from this prompt; the runtime still checks all active scopes.");
        }

        return text.ToString();
    }

    private static bool MatchesFolderScope(
        NIRAAuthorityScope scope,
        NIRAAuthorityOperation operation)
    {
        if (string.IsNullOrWhiteSpace(scope.RootPath) ||
            !Path.IsPathFullyQualified(scope.RootPath))
        {
            return false;
        }

        if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                operation.Request.CapabilityId))
        {
            return
                !string.IsNullOrWhiteSpace(operation.WorkingDirectory)
                &&
                NIRACapabilityRequestPolicy.IsWithin(
                    operation.WorkingDirectory,
                    scope.RootPath)
                &&
                NIRAAuthorityExecutionProfiles.MatchesAny(
                    operation,
                    scope.ExecutionProfileIds,
                    out _);
        }

        return
            operation.Paths.Length > 0
            &&
            (operation.Request.CapabilityId.StartsWith(
                 "filesystem.",
                 StringComparison.Ordinal)
             ||
             operation.Request.CapabilityId ==
                 NIRACapabilityIds.HttpDownload)
            &&
            operation.Paths.All(
                path =>
                    NIRACapabilityRequestPolicy.IsWithin(
                        path,
                        scope.RootPath))
            &&
            (scope.Origin.Length == 0
             ||
             string.Equals(
                 scope.Origin,
                 operation.Origin,
                 StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveExecutionGrantRoot(
        NIRAAuthorityOperation operation,
        NIRAApprovalResponse response)
    {
        string requested =
            string.IsNullOrWhiteSpace(response.FolderRoot)
                ? operation.WorkingDirectory
                : response.FolderRoot;

        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new InvalidOperationException(
                "A bounded project execution profile requires an explicit working project folder.");
        }

        string root =
            NIRACapabilityRequestPolicy.NormalizeLocalPath(
                requested);

        _policy.ValidatePath(root);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "The project execution root must already exist.");
        }

        if (NIRACapabilityRequestPolicy.IsWithin(
                root,
                _store.ProtectedDirectory))
        {
            throw new InvalidOperationException(
                "The authority directory cannot be granted.");
        }

        if (!string.IsNullOrWhiteSpace(operation.WorkingDirectory) &&
            !NIRACapabilityRequestPolicy.IsWithin(
                operation.WorkingDirectory,
                root))
        {
            throw new InvalidOperationException(
                "The requested command working directory is outside the selected project boundary.");
        }

        return root;
    }

    private string ResolveGrantRoot(
        NIRAAuthorityOperation operation,
        NIRAApprovalResponse response)
    {
        string root =
            NIRACapabilityRequestPolicy.NormalizeLocalPath(
                string.IsNullOrWhiteSpace(response.FolderRoot)
                    ? operation.SuggestedRoot
                    : response.FolderRoot);

        _policy.ValidatePath(root);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "The delegated project/folder root must already exist.");
        }

        if (NIRACapabilityRequestPolicy.IsWithin(root, _store.ProtectedDirectory))
        {
            throw new InvalidOperationException(
                "The authority directory cannot be granted.");
        }

        return root;
    }
}

