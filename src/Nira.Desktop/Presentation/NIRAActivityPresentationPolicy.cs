using System;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Human-readable labels for persisted agent-loop events. No extra model
/// request, inferred chain-of-thought, tool arguments, credentials or private
/// provider output. Unknown IDs are never printed verbatim.
/// </summary>
public static class NIRAActivityPresentationPolicy
{
    public static string ToolTitle(string? toolId)
    {
        string id = (toolId ?? string.Empty).ToLowerInvariant();
        if (id == "nira.capabilities.discover") return "Finding an available capability";
        if (id == "nira.capabilities.observe") return "Using a read-only capability";
        if (id == "nira.capabilities.execute") return "Running an authorized action";
        if (id.Contains("video.search", StringComparison.Ordinal)) return "Finding relevant videos";
        if (id.Contains("web.search", StringComparison.Ordinal)) return "Searching the web";
        if (id.Contains("image.search", StringComparison.Ordinal) ||
            id.Contains("image.retrieve", StringComparison.Ordinal)) return "Finding relevant images";
        if (id.Contains("image.generate", StringComparison.Ordinal)) return "Generating an image";
        if (id.Contains("docx", StringComparison.Ordinal)) return "Working with a Word document";
        if (id.Contains("browser", StringComparison.Ordinal)) return "Working in the browser";
        if (id.Contains("vision", StringComparison.Ordinal) ||
            id.Contains("screen", StringComparison.Ordinal)) return "Examining visual information";
        if (id.Contains("filesystem", StringComparison.Ordinal) ||
            id.Contains("file", StringComparison.Ordinal)) return "Working with files";
        if (id.Contains("math", StringComparison.Ordinal)) return "Checking a calculation";
        if (id.Contains("goal", StringComparison.Ordinal) ||
            id.Contains("lifecycle", StringComparison.Ordinal)) return "Updating the work plan";
        return "Using an available capability";
    }

    // Entries whose meanings are grounded by the agent's persisted outcome.
    public static bool IsKnown(string? code) => code is
        "RUN_ACTIVE" or "REASONING_STARTED" or "TOOL_PROPOSED" or
        "TOOL_DISPATCHED" or "TOOL_SUCCEEDED" or "TOOL_FAILED" or
        "TOOL_CONFIRMED_NO_EFFECT" or "AUTHORIZATION_REQUIRED" or
        "RECONCILIATION_REQUIRED" or "RUN_COMPLETED" or
        "RUN_INCOMPLETE" or "RUN_CANCELLED";

    public static string ResultLabel(string code) => code switch
    {
        "TOOL_SUCCEEDED" => "Result received",
        "TOOL_FAILED" => "The operation failed",
        "TOOL_CONFIRMED_NO_EFFECT" => "Rejected without changing anything",
        "AUTHORIZATION_REQUIRED" => "Waiting for authorization",
        "RECONCILIATION_REQUIRED" => "Outcome requires verification before retrying",
        "RUN_INCOMPLETE" => "The task was not completed",
        "RUN_CANCELLED" => "The task was cancelled",
        _ => ""
    };

    public static int RunSelfCheck()
    {
        int checks = 0;
        if (ToolTitle("nira.direct.web.search") != "Searching the web")
            throw new InvalidOperationException("Research activity label mismatch.");
        checks++;
        if (ToolTitle("nira.direct.filesystem.docx.report.create") != "Working with a Word document")
            throw new InvalidOperationException("Document activity label mismatch.");
        checks++;
        if (ToolTitle("PRIVATE_PASSWORD_XYZ") == "PRIVATE_PASSWORD_XYZ")
            throw new InvalidOperationException("Private tool IDs must never be printed.");
        checks++;
        if (!IsKnown("TOOL_SUCCEEDED") || IsKnown("REASONING_PRIVATE_TEXT"))
            throw new InvalidOperationException("Untrusted activity code accepted.");
        checks++;
        if (ResultLabel("TOOL_FAILED") != "The operation failed" ||
            ResultLabel("TOOL_SUCCEEDED") != "Result received")
            throw new InvalidOperationException("Activity outcome was overstated.");
        checks++;
        return checks;
    }
}
