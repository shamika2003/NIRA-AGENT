using System.IO;

namespace NIRAAgent.Capabilities;

/// <summary>
/// Privacy-safe, evidence-based failure classification for filesystem tools.
/// Diagnostic only: never authorizes, retries, remaps, or performs a file action.
/// </summary>
internal static class NIRAFilesystemFailureDiagnostic
{
    internal static string Classify(Exception error) => error switch
    {
        UnauthorizedAccessException => "AccessDenied",
        IOException io when io.HResult == unchecked((int)0x80070005) => "AccessDenied",
        DirectoryNotFoundException => "DirectoryUnavailable",
        FileNotFoundException => "FileUnavailable",
        IOException => "FilesystemIOError",
        ArgumentException => "InvalidArguments",
        InvalidOperationException => "InvalidOperation",
        _ => "OtherFailure"
    };

    internal static string Format(string capabilityId, Exception error, bool dispatched)
    {
        // Never persist exception.Message, arguments, paths, or document contents.
        return $"Capability={capabilityId} " +
            $"Stage={(dispatched ? "AfterDispatch" : "BeforeDispatch")} " +
            $"Kind={Classify(error)} Exception={error.GetType().Name} " +
            $"HResult=0x{error.HResult:X8} " +
            "AuthorizationUnchanged=True AutoRetry=False";
    }
}
