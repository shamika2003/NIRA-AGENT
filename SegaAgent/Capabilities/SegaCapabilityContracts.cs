/*
 * filename: SegaCapabilityContracts.cs
 */

using System.Text;
using System.Text.Json;

namespace SegaAgent.Capabilities;


public enum SegaCapabilityRisk
{
    Observe,

    Modify,

    Execute,

    Destructive
}


public enum SegaCapabilityResultStatus
{
    Succeeded,

    Failed,

    AuthorizationRequired,

    Rejected
}


public enum SegaCapabilityAuthorizationStatus
{
    Allowed,

    AuthorizationRequired,

    Denied
}


public static class SegaCapabilityIds
{
    public const string FileRead =
        "filesystem.read";


    public const string DirectoryList =
        "filesystem.list";


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


    public const string VisionCapture =
        "vision.capture";


    public const string VisionInspect =
        "vision.inspect";
}


public sealed record SegaCapabilityParameterDescriptor
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


public sealed record SegaCapabilityDescriptor
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


    public SegaCapabilityRisk DefaultRisk
    {
        get;
        init;
    } =
        SegaCapabilityRisk.Observe;


    public IReadOnlyList<SegaCapabilityParameterDescriptor> Parameters
    {
        get;
        init;
    } =
        Array.Empty<SegaCapabilityParameterDescriptor>();


    public SegaCapabilityDescriptor Normalize()
    {
        string id =
            Id?.Trim().ToLowerInvariant()
            ?? string.Empty;


        if (string.IsNullOrWhiteSpace(
                id))
        {
            throw new InvalidOperationException(
                "A Sega capability requires an ID.");
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


        SegaCapabilityParameterDescriptor[] parameters =
            (Parameters
                ?? Array.Empty<SegaCapabilityParameterDescriptor>())
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


public sealed record SegaCapabilityRequest
{
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


    public SegaCapabilityRequest Normalize()
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


        return this with
        {
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
        SegaCapabilityRequest normalized =
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


public sealed record SegaCapabilityAuthorizationDecision
{
    public Guid? ScopeId { get; init; }
    public string RequestFingerprint { get; init; } = string.Empty;

    public SegaCapabilityAuthorizationStatus Status
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


    public static SegaCapabilityAuthorizationDecision Allow(
        string reason)
    {
        return new SegaCapabilityAuthorizationDecision
        {
            Status =
                SegaCapabilityAuthorizationStatus.Allowed,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }


    public static SegaCapabilityAuthorizationDecision RequireAuthorization(
        string reason)
    {
        return new SegaCapabilityAuthorizationDecision
        {
            Status =
                SegaCapabilityAuthorizationStatus.AuthorizationRequired,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }


    public static SegaCapabilityAuthorizationDecision Deny(
        string reason)
    {
        return new SegaCapabilityAuthorizationDecision
        {
            Status =
                SegaCapabilityAuthorizationStatus.Denied,

            Reason =
                reason?.Trim()
                ?? string.Empty
        };
    }
}


public sealed record SegaCapabilityHandlerResult
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


    public bool ChangedSystemState
    {
        get;
        init;
    }
}


public sealed record SegaCapabilityResult
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


    public SegaCapabilityRisk Risk
    {
        get;
        init;
    }


    public SegaCapabilityResultStatus Status
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
        SegaCapabilityResultStatus.Succeeded;
}


public interface ISegaCapabilityHandler
{
    SegaCapabilityDescriptor Descriptor
    {
        get;
    }


    SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request);


    Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default);
}


public interface ISegaCapabilityAuthorizer
{
    string BuildCognitionContext() => "A runtime-owned authorizer checks every request.";

    Task<SegaCapabilityAuthorizationDecision> ValidateForDispatchAsync(
        SegaCapabilityDescriptor descriptor, SegaCapabilityRisk risk,
        SegaCapabilityRequest request, SegaCapabilityAuthorizationDecision decision,
        CancellationToken cancellationToken = default) => Task.FromResult(decision);

    Task<SegaCapabilityAuthorizationDecision> AuthorizeAsync(
        SegaCapabilityDescriptor descriptor,
        SegaCapabilityRisk risk,
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default);
}