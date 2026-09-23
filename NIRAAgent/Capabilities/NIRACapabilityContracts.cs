/*
 * filename: NIRACapabilityContracts.cs
 */

using System.Text;
using System.Text.Json;

namespace NIRAAgent.Capabilities;


public enum NIRACapabilityRisk
{
    Observe,

    Modify,

    Execute,

    Destructive
}


public enum NIRACapabilityResultStatus
{
    Succeeded,

    Failed,

    AuthorizationRequired,

    Rejected
}


public enum NIRACapabilityAuthorizationStatus
{
    Allowed,

    AuthorizationRequired,

    Denied
}


public static class NIRACapabilityIds
{
    public const string FileRead =
        "filesystem.read";


    public const string DirectoryList =
        "filesystem.list";


    public const string FileLocate =
        "filesystem.locate";


    public const string FileMetadata =
        "filesystem.metadata";


    public const string FileWrite =
        "filesystem.write";


    public const string FileCopy =
        "filesystem.copy";


    public const string FileMove =
        "filesystem.move";


    public const string FileDelete =
        "filesystem.delete";


    public const string DirectoryCreate =
        "filesystem.directory.create";


    public const string ApplicationResolve =
        "application.resolve";


    public const string ProcessList =
        "process.list";


    public const string ProcessStart =
        "process.start";


    public const string ProcessStop =
        "process.stop";


    public const string ShellExecute =
        "shell.execute";


    public const string HttpRequest =
        "http.request";


    public const string HttpDownload =
        "http.download";


    public const string BrowserSessionOpen =
        "browser.session.open";


    public const string BrowserSessionClose =
        "browser.session.close";


    public const string BrowserCurrent =
        "browser.current";

    public const string BrowserPageSelect =
        "browser.page.select";


    public const string BrowserNavigate =
        "browser.navigate";


    public const string BrowserInspect =
        "browser.inspect";


    public const string BrowserFollow =
        "browser.follow";


    public const string BrowserClick =
        "browser.click";


    public const string BrowserFill =
        "browser.fill";


    public const string BrowserSelect =
        "browser.select";


    public const string BrowserWait =
        "browser.wait";


    public const string BrowserScreenshot =
        "browser.screenshot";


    public const string BrowserRequest =
        "browser.request";


    public const string BrowserDownload =
        "browser.download";

    public const string BrowserUpload =
        "browser.upload";


    public const string BrowserAuthenticate =
        "browser.authenticate";

    public const string BrowserAccounts = "browser.accounts";

    public const string BrowserRecoveryResume =
        "browser.recovery.resume";


    public const string TemporalRelate = "temporal.relate";

    public const string VisionCapture =
        "vision.capture";


    public const string VisionInspect =
        "vision.inspect";
}


public sealed record NIRACapabilityParameterDescriptor
{
    public string Name
    {
        get;
        init;
    } =
        string.Empty;


    public string Type
    {
        get;
        init;
    } =
        "string";


    public bool Required
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


public sealed record NIRACapabilityDescriptor
{
    public string Id
    {
        get;
        init;
    } =
        string.Empty;


    public string Description
    {
        get;
        init;
    } =
        string.Empty;


    public NIRACapabilityRisk DefaultRisk
    {
        get;
        init;
    } =
        NIRACapabilityRisk.Observe;


    public IReadOnlyList<NIRACapabilityParameterDescriptor> Parameters
    {
        get;
        init;
    } =
        Array.Empty<NIRACapabilityParameterDescriptor>();


    public NIRACapabilityDescriptor Normalize()
    {
        string id =
            Id?.Trim().ToLowerInvariant()
            ?? string.Empty;


        if (string.IsNullOrWhiteSpace(
                id))
        {
            throw new InvalidOperationException(
                "A NIRA capability requires an ID.");
        }


        string description =
            Description?.Trim()
            ?? string.Empty;


        if (description.Length >
            1000)
        {
            throw new InvalidOperationException(
                "Capability description is too long.");
        }


        NIRACapabilityParameterDescriptor[] parameters =
            (Parameters
                ?? Array.Empty<NIRACapabilityParameterDescriptor>())
            .Where(
                parameter =>
                    parameter !=
                    null)
            .Select(
                parameter =>
                    parameter with
                    {
                        Name =
                            parameter.Name?.Trim()
                            ?? string.Empty,

                        Type =
                            string.IsNullOrWhiteSpace(
                                    parameter.Type)
                                ? "string"
                                : parameter.Type.Trim(),

                        Description =
                            parameter.Description?.Trim()
                            ?? string.Empty
                    })
            .Where(
                parameter =>
                    !string.IsNullOrWhiteSpace(
                        parameter.Name))
            .GroupBy(
                parameter =>
                    parameter.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                    group.First())
            .Take(
                32)
            .ToArray();


        return this with
        {
            Id =
                id,

            Description =
                description,

            Parameters =
                parameters
        };
    }
}


public sealed record NIRACapabilityRequest
{
    // Trusted execution-only flag; never a model-visible capability argument.
    // Protects implicitly delegated creation from overwriting a file that
    // appears after the final authorization check.
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool RequireCreateNew { get; init; }

    public Guid RequestId
    {
        get;
        init;
    }


    public string CapabilityId
    {
        get;
        init;
    } =
        string.Empty;


    public JsonElement Arguments
    {
        get;
        init;
    }


    public string Reason
    {
        get;
        init;
    } =
        string.Empty;

    // A bounded, pre-grounded continuation of branch-owned capability work.
    // The ordinary capability runtime NEVER executes these itself. Only the
    // branch runner can do so, with per-step authorization and stop-on-failure.
    // This is persisted with the existing branch capability-request JSON.
    public IReadOnlyList<NIRACapabilityRequest> ContinuationRequests { get; init; } =
        Array.Empty<NIRACapabilityRequest>();

    public NIRACapabilityRequest Normalize()
    {
        string capabilityId =
            CapabilityId?.Trim().ToLowerInvariant()
            ?? string.Empty;


        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            throw new InvalidOperationException(
                "A capability request requires a capabilityId.");
        }


        if (capabilityId.Length >
            160)
        {
            throw new InvalidOperationException(
                "Capability ID is too long.");
        }


        JsonElement arguments =
            NormalizeArguments(
                Arguments);


        string reason =
            Reason?.Trim()
            ?? string.Empty;


        if (reason.Length >
            1600)
        {
            throw new InvalidOperationException(
                "Capability request reason is too long.");
        }


        // The model may specify up to three additional already-grounded
        // operations, but never a recursively expanding work graph.
        IReadOnlyList<NIRACapabilityRequest> requestedContinuation =
            ContinuationRequests ?? Array.Empty<NIRACapabilityRequest>();
        if (requestedContinuation.Count > 3)
            throw new InvalidOperationException(
                "A capability continuation can contain at most three steps.");
        List<NIRACapabilityRequest> continuation = new();
        foreach (NIRACapabilityRequest child in requestedContinuation)
        {
            if (child == null ||
                (child.ContinuationRequests?.Count ?? 0) != 0)
                throw new InvalidOperationException(
                    "Nested capability continuations are not permitted.");
            continuation.Add(child.Normalize());
        }

        return this with
        {
            ContinuationRequests = continuation,
            RequestId =
                RequestId ==
                    Guid.Empty
                    ? Guid.NewGuid()
                    : RequestId,

            CapabilityId =
                capabilityId,

            Arguments =
                arguments,

            Reason =
                reason
        };
    }


    public string BuildSignature()
    {
        NIRACapabilityRequest normalized =
            Normalize();


        StringBuilder builder =
            new();


        builder.Append(
            normalized.CapabilityId);


        if (normalized.Arguments.ValueKind ==
            JsonValueKind.Object)
        {
            foreach (
                JsonProperty property
                in normalized.Arguments
                    .EnumerateObject()
                    .OrderBy(
                        property =>
                            property.Name,
                        StringComparer.OrdinalIgnoreCase))
            {
                builder.Append('|');
                builder.Append(
                    property.Name.ToLowerInvariant());
                builder.Append('=');
                builder.Append(
                    property.Value.GetRawText());
            }
        }


        // Changing a continuation is a distinct work item. Reason and
        // transient RequestId remain excluded from executable identity.
        for (int index = 0; index < normalized.ContinuationRequests.Count; index++)
        {
            builder.Append("|continuation[");
            builder.Append(index);
            builder.Append("]=");
            builder.Append(normalized.ContinuationRequests[index].BuildSignature());
        }

        return builder.ToString();
    }


    private static JsonElement NormalizeArguments(
        JsonElement arguments)
    {
        if (arguments.ValueKind is
            JsonValueKind.Undefined or
            JsonValueKind.Null)
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    "{}");


            return document.RootElement.Clone();
        }


        if (arguments.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Capability request arguments must be a JSON object.");
        }


        string raw =
            arguments.GetRawText();


        if (raw.Length >
            50000)
        {
            throw new InvalidOperationException(
                "Capability request arguments are too large.");
        }


        return arguments.Clone();
    }
}


public sealed record NIRACapabilityAuthorizationDecision
{
    public Guid? ScopeId { get; init; }
    public string RequestFingerprint { get; init; } = string.Empty;
    // DirectUserTask authority must be revalidated at dispatch. It never
    // persists as a reusable scope or becomes a model-controlled argument.
    public bool DerivedFromDirectUserRequest { get; init; }
    public string AuthorityBasis { get; init; } = string.Empty;

    public NIRACapabilityAuthorizationStatus Status
    {
        get;
        init;
    }


    public string Reason
    {
        get;
        init;
    } =
        string.Empty;


    public static NIRACapabilityAuthorizationDecision Allow(
        string reason)
    {
        return new NIRACapabilityAuthorizationDecision
        {
            Status =
                NIRACapabilityAuthorizationStatus.Allowed,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }


    public static NIRACapabilityAuthorizationDecision RequireAuthorization(
        string reason)
    {
        return new NIRACapabilityAuthorizationDecision
        {
            Status =
                NIRACapabilityAuthorizationStatus.AuthorizationRequired,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }


    public static NIRACapabilityAuthorizationDecision Deny(
        string reason)
    {
        return new NIRACapabilityAuthorizationDecision
        {
            Status =
                NIRACapabilityAuthorizationStatus.Denied,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }
}



public sealed record NIRACapabilityVisualArtifact
{
    public Guid? EvidenceId { get; init; }

    public string LocalPath { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Caption { get; init; } = string.Empty;

    public bool PresentToUser { get; init; }

    public string Surface { get; init; } = "Auto";

    public NIRACapabilityVisualArtifact Normalize()
    {
        bool hasEvidence = EvidenceId.HasValue && EvidenceId.Value != Guid.Empty;
        bool hasPath = !string.IsNullOrWhiteSpace(LocalPath);

        if (!hasEvidence && !hasPath)
            throw new InvalidOperationException(
                "A capability visual artifact requires an evidenceId or localPath.");

        string surface = string.IsNullOrWhiteSpace(Surface)
            ? "Auto"
            : Surface.Trim();

        if (surface is not ("Auto" or "InlineOnly" or "ToastOnly" or "InlineAndToast"))
            surface = "Auto";

        return this with
        {
            EvidenceId = hasEvidence ? EvidenceId : null,
            LocalPath = hasEvidence ? string.Empty : LocalPath.Trim(),
            Title = NormalizeText(Title, 180),
            Caption = NormalizeText(Caption, 1000),
            Surface = surface
        };
    }

    private static string NormalizeText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string clean = string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        return clean.Length <= maximum
            ? clean
            : clean[..maximum];
    }
}


public sealed record NIRACapabilityHandlerResult
{
    public bool Succeeded { get; init; } = true;
    public int? ExitCode { get; init; }
    public int? HttpStatusCode { get; init; }

    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    public string Output
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<NIRACapabilityVisualArtifact> VisualArtifacts
    {
        get;
        init;
    } =
        Array.Empty<NIRACapabilityVisualArtifact>();


    public bool ChangedSystemState
    {
        get;
        init;
    }
}


public sealed record NIRACapabilityResult
{
    public Guid? AuthorizationScopeId { get; init; }
    public Guid? AuditAttemptId { get; init; }
    public bool AuditRecorded { get; init; }
    public bool OutcomeUncertain { get; init; }
    public int? ExitCode { get; init; }
    public int? HttpStatusCode { get; init; }

    public Guid RequestId
    {
        get;
        init;
    }


    public string CapabilityId
    {
        get;
        init;
    } =
        string.Empty;


    public NIRACapabilityRisk Risk
    {
        get;
        init;
    }


    public NIRACapabilityResultStatus Status
    {
        get;
        init;
    }


    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    public string Output
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<NIRACapabilityVisualArtifact> VisualArtifacts
    {
        get;
        init;
    } =
        Array.Empty<NIRACapabilityVisualArtifact>();


    public bool ChangedSystemState
    {
        get;
        init;
    }


    public DateTimeOffset StartedAtUtc
    {
        get;
        init;
    }


    public DateTimeOffset FinishedAtUtc
    {
        get;
        init;
    }


    public bool Succeeded =>
        Status ==
        NIRACapabilityResultStatus.Succeeded;
}


public interface INIRACapabilityHandler
{
    NIRACapabilityDescriptor Descriptor
    {
        get;
    }


    NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request);


    Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default);
}


public interface INIRACapabilityAuthorizer
{
    string BuildCognitionContext() => "A runtime-owned authorizer checks every request.";

    Task<NIRACapabilityAuthorizationDecision> ValidateForDispatchAsync(
        NIRACapabilityDescriptor descriptor, NIRACapabilityRisk risk,
        NIRACapabilityRequest request, NIRACapabilityAuthorizationDecision decision,
        CancellationToken cancellationToken = default) => Task.FromResult(decision);

    Task<NIRACapabilityAuthorizationDecision> AuthorizeAsync(
        NIRACapabilityDescriptor descriptor,
        NIRACapabilityRisk risk,
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default);
}