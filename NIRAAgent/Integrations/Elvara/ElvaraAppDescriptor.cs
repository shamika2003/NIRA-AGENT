/*
 * filename: ElvaraAppDescriptor.cs
 */

namespace NIRAAgent.Integrations.Elvara;


// =============================================================
// ELVARA APPLICATION DESCRIPTOR
//
// ELVARA is the product brand.
//
// NIRA is the single intelligence/runtime. Other trusted ELVARA
// products may expose an "Ask NIRA" surface without creating
// another NIRA instance or owning their own LLM connection.
// =============================================================

public sealed record ElvaraAppDescriptor
{
    public required string AppId
    {
        get;
        init;
    }


    public required string DisplayName
    {
        get;
        init;
    }


    public required string ProductName
    {
        get;
        init;
    }


    // Whether this application may expose an embedded Ask NIRA
    // surface. That surface remains scoped to this application.
    public bool AllowEmbeddedNIRA
    {
        get;
        init;
    } =
        true;


    // Whether NIRA may request current authoritative information
    // from this application's registered connector.
    public bool AllowNIRAQuery
    {
        get;
        init;
    } =
        true;


    public string Description
    {
        get;
        init;
    } =
        string.Empty;
}
