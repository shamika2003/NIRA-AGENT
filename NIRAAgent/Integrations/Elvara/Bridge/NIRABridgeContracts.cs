/*
 * filename: NIRABridgeContracts.cs
 */

namespace NIRAAgent.Integrations.Elvara.Bridge;


// =============================================================
// PUBLIC NIRA LOCAL BRIDGE CONTRACTS
// =============================================================

public sealed record NIRABridgeHealthResponse
{
    public string Service
    {
        get;
        init;
    } =
        "NIRA Local Bridge";


    public string Version
    {
        get;
        init;
    } =
        NIRABridgeOptions.Version;


    public string Brand
    {
        get;
        init;
    } =
        "ELVARA";


    public string Agent
    {
        get;
        init;
    } =
        "NIRA";


    public string Status
    {
        get;
        init;
    } =
        "ok";


    public string Address
    {
        get;
        init;
    } =
        NIRABridgeOptions.BaseUrl;


    public bool LocalhostOnly
    {
        get;
        init;
    } =
        true;


    public bool ChatEnabled
    {
        get;
        init;
    } =
        true;
}


public sealed record NIRABridgeAppResponse
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


    public bool AllowEmbeddedNIRA
    {
        get;
        init;
    }


    public bool AllowNIRAQuery
    {
        get;
        init;
    }


    public string Description
    {
        get;
        init;
    } =
        string.Empty;
}


public sealed record NIRABridgeAppsResponse
{
    public required IReadOnlyList<NIRABridgeAppResponse> Apps
    {
        get;
        init;
    }
}


// =============================================================
// EMBEDDED ASK-NIRA CHAT
//
// The application sends only the user's message plus UI context.
// The application does not call an LLM and does not provide
// authoritative domain facts through this contract.
// =============================================================

public sealed record NIRABridgeChatRequest
{
    public required string Message
    {
        get;
        init;
    }


    public string Surface
    {
        get;
        init;
    } =
        "embedded";


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
}


public sealed record NIRABridgeChatResponse
{
    public required string AppId
    {
        get;
        init;
    }


    public required string Reply
    {
        get;
        init;
    }


    public Guid? RunId
    {
        get;
        init;
    }


    public string Scope
    {
        get;
        init;
    } =
        string.Empty;
}


public sealed record NIRABridgeErrorResponse
{
    public required string Error
    {
        get;
        init;
    }


    public required string Message
    {
        get;
        init;
    }
}
