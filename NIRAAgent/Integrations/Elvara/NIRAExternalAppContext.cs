/*
 * filename: NIRAExternalAppContext.cs
 */

namespace NIRAAgent.Integrations.Elvara;


// =============================================================
// EXTERNAL ELVARA APPLICATION CONTEXT
//
// Describes WHERE the user is currently talking to NIRA.
//
// This is surface/context metadata only. Application-supplied
// values must not be treated as authoritative account, trading,
// system or domain facts. NIRA retrieves authoritative current
// data through the registered application connector when needed.
// =============================================================

public sealed record NIRAExternalAppContext
{
    public required string AppId
    {
        get;
        init;
    }


    public required string Surface
    {
        get;
        init;
    }


    public string Page
    {
        get;
        init;
    } =
        string.Empty;


    public string SelectedEntity
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyDictionary<string, string> Metadata
    {
        get;
        init;
    } =
        new Dictionary<string, string>();
}
