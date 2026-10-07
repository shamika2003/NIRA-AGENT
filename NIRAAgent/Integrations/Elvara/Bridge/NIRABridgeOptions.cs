/*
 * filename: NIRABridgeOptions.cs
 */

namespace NIRAAgent.Integrations.Elvara.Bridge;


// =============================================================
// NIRA LOCAL BRIDGE OPTIONS
//
// The bridge is intentionally bound only to IPv4 loopback.
// It is not a LAN service and must not be exposed externally.
// =============================================================

public static class NIRABridgeOptions
{
    public const string Version =
        "1.0.0";


    public const string Host =
        "127.0.0.1";


    public const int Port =
        8766;


    public const string BaseUrl =
        "http://127.0.0.1:8766";


    public const string ListenerPrefix =
        "http://127.0.0.1:8766/";
}
