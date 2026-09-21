namespace NIRAAgent.Authorization;

// Trusted credential bridge. It is called only from trusted runtime handlers;
// no secret value is ever inserted into capability arguments or cognition.
public sealed class NIRACredentialBroker
{
    private readonly NIRACredentialStore _store;

    private Func<
        NIRACredentialPromptRequest,
        CancellationToken,
        Task<NIRACredentialPromptResponse?>>? _promptHandler;

    public NIRACredentialBroker(NIRACredentialStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void SetPromptHandler(
        Func<
            NIRACredentialPromptRequest,
            CancellationToken,
            Task<NIRACredentialPromptResponse?>> handler)
    {
        _promptHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public IReadOnlyList<(string Origin, string AccountLabel, string? LoginRoute)> ReadKnownAccounts() =>
        _store.ReadAllMetadata()
            .Select(meta => (meta.Origin, meta.AccountLabel,
                _store.ReadObservedLoginRoute(meta.Id)))
            .ToArray();

    public void RecordObservedLoginRoute(Guid id, string origin, string url) =>
        _store.RecordObservedLoginRoute(id, origin, url);

    public async Task<NIRACredentialMaterial?> ResolveAsync(
        string origin,
        string? requestedAccountLabel,
        string reason,
        CancellationToken cancellationToken = default,
        bool forceReplacement = false,
        string? observedLoginRoute = null)
    {
        string normalizedOrigin = NIRACredentialStore.NormalizeOrigin(origin);
        string requested = requestedAccountLabel?.Trim() ?? string.Empty;

        IReadOnlyList<NIRACredentialMetadata> allForOrigin =
            _store.ReadForOrigin(normalizedOrigin);

        // An origin can host several distinct account roles. Match the actual
        // inspected login route BEFORE reading any Windows Credential Manager
        // secret. Accounts bound to an admin/login route never silently fill a
        // student/login form on the same host, and vice versa.
        string? observedRoute = CanonicalObservedRoute(observedLoginRoute, normalizedOrigin);
        var routeMatches = allForOrigin
            .Select(metadata => (Metadata: metadata,
                Route: CanonicalObservedRoute(
                    _store.ReadObservedLoginRoute(metadata.Id), normalizedOrigin)))
            .ToArray();
        NIRACredentialMetadata[] exactRoute = observedRoute == null
            ? []
            : routeMatches
                .Where(item => string.Equals(item.Route, observedRoute,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Metadata).ToArray();
        NIRACredentialMetadata[] eligible = observedRoute == null
            ? allForOrigin.ToArray()
            : exactRoute.Length > 0
                ? exactRoute
                : routeMatches.Where(item => item.Route == null)
                    .Select(item => item.Metadata).ToArray();

        if (!string.IsNullOrWhiteSpace(requested))
        {
            NIRACredentialMetadata? explicitlySelected = allForOrigin
                .FirstOrDefault(item => string.Equals(item.AccountLabel,
                    requested, StringComparison.OrdinalIgnoreCase));
            if (explicitlySelected != null && observedRoute != null &&
                !eligible.Any(item => item.Id == explicitlySelected.Id))
            {
                // The model chose a label for a different role. Never fill it,
                // never navigate to its route, and never throw before the UI.
                // The trusted user can provide a correct account for THIS form.
                requested = string.Empty;
                reason = "The previously selected saved account is for a different " +
                    "login page. Enter or select the account for the current page " +
                    "in NIRA's secure credential window. " + reason;
                System.Diagnostics.Debug.WriteLine(
                    "[AuthFlow] Saved account route mismatch -> secure prompt for current page");
            }
        }

        IReadOnlyList<NIRACredentialMetadata> existing = eligible;
        if (!forceReplacement && !string.IsNullOrWhiteSpace(requested))
        {
            NIRACredentialMetadata? exact = existing.FirstOrDefault(item =>
                string.Equals(item.AccountLabel, requested,
                    StringComparison.OrdinalIgnoreCase));
            // Legacy unbound credentials need a trusted user selection on a
            // role-specific page; a model-selected label alone is insufficient.
            if (exact != null && (observedRoute == null ||
                exactRoute.Any(item => item.Id == exact.Id)))
                return _store.Read(exact.Id);
        }
        else if (!forceReplacement && exactRoute.Length == 1 &&
                 string.IsNullOrWhiteSpace(requested))
        {
            // An explicit page-route match outranks an unrelated credential
            // with the same origin and an unknown or different role.
            return _store.Read(exactRoute[0].Id);
        }
        else if (!forceReplacement && observedRoute == null &&
                 existing.Count == 1 && allForOrigin.Count == 1)
        {
            return _store.Read(existing[0].Id);
        }

        if (_promptHandler == null)
        {
            throw new InvalidOperationException(
                "NIRA needs a website credential, but no trusted credential UI is attached.");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[AuthFlow] SECURE PROMPT | RouteKnown={observedRoute != null} | " +
            $"MatchingRoute={exactRoute.Length} | UserChoices={existing.Count} | " +
            $"Refresh={forceReplacement}");
        NIRACredentialPromptResponse? response =
            await _promptHandler(
                new NIRACredentialPromptRequest
                {
                    Origin = normalizedOrigin,
                    RequestedAccountLabel = requested,
                    Reason = reason?.Trim() ?? string.Empty,
                    ExistingCredentials = existing,
                    ForceReplacement = forceReplacement
                },
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (response == null ||
            response.Choice == NIRACredentialPromptChoice.Cancel)
        {
            System.Diagnostics.Debug.WriteLine("[AuthFlow] SECURE PROMPT CANCELLED");
            return null;
        }
        System.Diagnostics.Debug.WriteLine(
            $"[AuthFlow] SECURE PROMPT ACCEPTED | Choice={response.Choice}");

        // A refresh cannot silently pick the same failing saved secret again.
        // The trusted UI must provide a new password; existing metadata is
        // presented only so Save & use can replace the SAME account label.
        if (forceReplacement && response.Choice == NIRACredentialPromptChoice.UseStored)
            throw new InvalidOperationException(
                "Credential refresh requires a newly entered secret in the trusted credential window. The previous stored credential was not retried.");

        // Save(accountLabel) overwrites an existing Windows credential of the
        // same origin+label. Guard that side effect BEFORE performing the save:
        // creating a student login must not destroy a saved admin login.
        if (response.Choice == NIRACredentialPromptChoice.SaveAndUse &&
            observedRoute != null)
        {
            string savingLabel = string.IsNullOrWhiteSpace(response.AccountLabel)
                ? response.Username.Trim()
                : response.AccountLabel.Trim();
            NIRACredentialMetadata? colliding = allForOrigin.FirstOrDefault(item =>
                string.Equals(item.AccountLabel, savingLabel,
                    StringComparison.OrdinalIgnoreCase));
            if (colliding != null &&
                !eligible.Any(item => item.Id == colliding.Id))
                throw new InvalidOperationException(
                    "That account label is already saved for a DIFFERENT login " +
                    "route on this website. The existing credential was NOT " +
                    "overwritten. Enter a distinct label for this login role " +
                    "in the trusted credential window.");
        }

        NIRACredentialMaterial resolved = response.Choice switch
        {
            NIRACredentialPromptChoice.UseStored =>
                ResolveStored(normalizedOrigin, response),

            NIRACredentialPromptChoice.UseOnce =>
                BuildTransient(normalizedOrigin, response),

            NIRACredentialPromptChoice.SaveAndUse =>
                SaveAndResolve(normalizedOrigin, response),

            _ =>
                throw new InvalidOperationException(
                    "Unknown secure credential resolution choice.")
        };
        // A trusted choice is still subject to the route boundary; never
        // assume that a UI selection grants authority to use a credential
        // on a different role's login document on the same origin.
        if (response.Choice == NIRACredentialPromptChoice.UseStored &&
            resolved.Metadata.Id != Guid.Empty && observedRoute != null &&
            !eligible.Any(item => item.Id == resolved.Metadata.Id))
            throw new InvalidOperationException(
                "Selected saved credential is not associated with the inspected " +
                "login route. No credential has been submitted.");
        return forceReplacement
            ? resolved with { FreshTrustedCredential = true }
            : resolved;
    }

    private static string? CanonicalObservedRoute(string? url, string origin)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin,
                StringComparison.OrdinalIgnoreCase)) return null;
        UriBuilder safe = new(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return safe.Uri.AbsoluteUri;
    }

    private NIRACredentialMaterial ResolveStored(
        string expectedOrigin,
        NIRACredentialPromptResponse response)
    {
        if (!response.CredentialId.HasValue ||
            response.CredentialId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The selected stored credential ID is invalid.");
        }

        NIRACredentialMaterial material =
            _store.Read(response.CredentialId.Value);

        if (!string.Equals(
                material.Metadata.Origin,
                expectedOrigin,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected stored credential does not belong to the requested website origin.");
        }

        return material;
    }

    private static NIRACredentialMaterial BuildTransient(
        string origin,
        NIRACredentialPromptResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Username) &&
            string.IsNullOrWhiteSpace(response.Secret))
        {
            throw new InvalidOperationException(
                "A one-time credential requires a username or secret.");
        }

        return new NIRACredentialMaterial
        {
            Metadata = new NIRACredentialMetadata
            {
                Id = Guid.Empty,
                Origin = origin,
                AccountLabel = string.IsNullOrWhiteSpace(response.AccountLabel)
                    ? response.Username.Trim()
                    : response.AccountLabel.Trim(),
                Username = response.Username.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastUsedAtUtc = DateTimeOffset.UtcNow
            },
            Username = response.Username,
            Secret = response.Secret
        };
    }

    private NIRACredentialMaterial SaveAndResolve(
        string origin,
        NIRACredentialPromptResponse response)
    {
        NIRACredentialMetadata metadata =
            _store.Save(
                origin,
                response.AccountLabel,
                response.Username,
                response.Secret);

        return _store.Read(metadata.Id);
    }
}
