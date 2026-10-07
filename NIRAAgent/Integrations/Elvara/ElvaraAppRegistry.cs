/*
 * filename: ElvaraAppRegistry.cs
 */

namespace NIRAAgent.Integrations.Elvara;


// =============================================================
// TRUSTED ELVARA APPLICATION REGISTRY
//
// This registry is owned by NIRA.
//
// Incoming bridge requests may reference an AppId, but they do
// not define trust or permissions. Only applications registered
// here are recognized as ELVARA integration peers.
// =============================================================

public sealed class ElvaraAppRegistry
{
    private readonly Dictionary<string, ElvaraAppDescriptor>
        _apps =
            new(
                StringComparer.OrdinalIgnoreCase);


    public ElvaraAppRegistry()
    {
        RegisterBuiltInApps();
    }


    // =========================================================
    // BUILT-IN ELVARA APPLICATIONS
    // =========================================================

    private void RegisterBuiltInApps()
    {
        Register(
            new ElvaraAppDescriptor
            {
                AppId =
                    "tradeai",

                DisplayName =
                    "TradeAI",

                ProductName =
                    "ELVARA TradeAI",

                AllowEmbeddedNIRA =
                    true,

                AllowNIRAQuery =
                    true,

                Description =
                    "ELVARA trading intelligence application. " +
                    "Embedded NIRA access is restricted to TradeAI context."
            });
    }


    // =========================================================
    // REGISTER
    // =========================================================

    private void Register(
        ElvaraAppDescriptor app)
    {
        ArgumentNullException.ThrowIfNull(
            app);


        string appId =
            NormalizeAppId(
                app.AppId);


        if (_apps.ContainsKey(
                appId))
        {
            throw new InvalidOperationException(
                $"ELVARA application '{appId}' is already registered.");
        }


        _apps.Add(
            appId,
            app with
            {
                AppId =
                    appId
            });
    }


    // =========================================================
    // LOOKUP
    // =========================================================

    public bool TryGet(
        string? appId,
        out ElvaraAppDescriptor? app)
    {
        app =
            null;


        if (string.IsNullOrWhiteSpace(
                appId))
        {
            return false;
        }


        return _apps.TryGetValue(
            NormalizeAppId(
                appId),
            out app);
    }


    public ElvaraAppDescriptor GetRequired(
        string appId)
    {
        if (!TryGet(
                appId,
                out ElvaraAppDescriptor? app)
            ||
            app ==
            null)
        {
            throw new InvalidOperationException(
                $"Unknown or untrusted ELVARA application '{appId}'.");
        }


        return app;
    }


    public IReadOnlyCollection<ElvaraAppDescriptor>
        GetAll()
    {
        return _apps
            .Values
            .OrderBy(
                app =>
                    app.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }


    // =========================================================
    // NORMALIZATION
    // =========================================================

    private static string NormalizeAppId(
        string appId)
    {
        string value =
            appId
                .Trim()
                .ToLowerInvariant();


        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "ELVARA AppId cannot be empty.",
                nameof(appId));
        }


        return value;
    }
}
