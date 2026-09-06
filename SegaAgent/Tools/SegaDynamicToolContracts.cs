/*
 * filename: SegaDynamicToolContracts.cs
 */

using System.Text;
using System.Text.Json;

using SegaAgent.Capabilities;

namespace SegaAgent.Tools;

// =============================================================
// DYNAMIC TOOL MODEL
//
// A dynamic tool is a validated, parameterized composition of
// trusted primitive capability calls. It is not executable C# and
// it never grants authority by itself.
// =============================================================

public enum SegaDynamicToolPersistence
{
    Temporary,
    Persistent
}

public enum SegaDynamicToolStatus
{
    Active,
    Disabled,
    Retired
}

public enum SegaDynamicToolProposalAction
{
    Create,
    Revise,
    Enable,
    Disable,
    Retire
}

public enum SegaDynamicToolApplyAction
{
    Rejected,
    Created,
    Revised,
    Enabled,
    Disabled,
    Retired,
    NoChange
}

public enum SegaDynamicToolFailurePolicy
{
    StopTool,
    Continue
}

public enum SegaDynamicValueSource
{
    Literal,
    Template,
    Parameter,
    StepOutput,
    StepSummary,
    StepExitCode,
    StepHttpStatusCode,
    StepSucceeded
}

public enum SegaDynamicConditionKind
{
    Always,
    ParameterEquals,
    StepSucceeded,
    StepFailed
}

public enum SegaDynamicToolStepStatus
{
    Succeeded,
    Failed,
    Skipped
}

public sealed record SegaDynamicToolParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public bool Required { get; init; } = true;
    public JsonElement? DefaultValue { get; init; }
    public string Description { get; init; } = string.Empty;

    public SegaDynamicToolParameterDefinition Normalize()
    {
        string name = Name?.Trim() ?? string.Empty;
        string type = string.IsNullOrWhiteSpace(Type)
            ? "string"
            : Type.Trim().ToLowerInvariant();
        string description = Description?.Trim() ?? string.Empty;

        if (name.Length > 64 || description.Length > 600)
            throw new InvalidOperationException("Dynamic tool parameter metadata is too long.");

        return this with
        {
            Name = name,
            Type = type,
            Description = description,
            DefaultValue = DefaultValue.HasValue
                ? DefaultValue.Value.Clone()
                : null
        };
    }
}

public sealed record SegaDynamicToolValueBinding
{
    public SegaDynamicValueSource Source { get; init; } = SegaDynamicValueSource.Literal;
    public JsonElement? Literal { get; init; }
    public string? Template { get; init; }
    public string? ParameterName { get; init; }
    public string? StepId { get; init; }

    public SegaDynamicToolValueBinding Normalize()
    {
        return this with
        {
            Literal = Literal.HasValue ? Literal.Value.Clone() : null,
            Template = NormalizeOptional(Template, 4000),
            ParameterName = NormalizeOptional(ParameterName, 64),
            StepId = NormalizeOptional(StepId, 64)
        };
    }

    private static string? NormalizeOptional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string clean = value.Trim();
        if (clean.Length > max)
            throw new InvalidOperationException("Dynamic tool binding metadata is too long.");
        return clean;
    }
}

public sealed record SegaDynamicToolCondition
{
    public SegaDynamicConditionKind Kind { get; init; } = SegaDynamicConditionKind.Always;
    public string? ParameterName { get; init; }
    public string? StepId { get; init; }
    public JsonElement? ExpectedValue { get; init; }

    public SegaDynamicToolCondition Normalize()
    {
        return this with
        {
            ParameterName = NormalizeOptional(ParameterName),
            StepId = NormalizeOptional(StepId),
            ExpectedValue = ExpectedValue.HasValue
                ? ExpectedValue.Value.Clone()
                : null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string clean = value.Trim();
        if (clean.Length > 64)
            throw new InvalidOperationException("Dynamic tool condition metadata is too long.");
        return clean;
    }
}

public sealed record SegaDynamicToolStep
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnStepIds { get; init; } = Array.Empty<string>();
    public Dictionary<string, SegaDynamicToolValueBinding> Arguments { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public SegaDynamicToolCondition Condition { get; init; } = new();
    public SegaDynamicToolFailurePolicy FailurePolicy { get; init; } =
        SegaDynamicToolFailurePolicy.StopTool;

    public SegaDynamicToolStep Normalize()
    {
        string id = Id?.Trim() ?? string.Empty;
        string capabilityId = CapabilityId?.Trim().ToLowerInvariant() ?? string.Empty;
        string description = Description?.Trim() ?? string.Empty;

        if (id.Length > 64 || capabilityId.Length > 160 || description.Length > 800)
            throw new InvalidOperationException("Dynamic tool step metadata is too long.");

        Dictionary<string, SegaDynamicToolValueBinding> arguments =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, SegaDynamicToolValueBinding> entry
                 in Arguments ?? new Dictionary<string, SegaDynamicToolValueBinding>())
        {
            string key = entry.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Dynamic tool step argument names cannot be empty.");
            if (!arguments.TryAdd(key, (entry.Value ?? new()).Normalize()))
                throw new InvalidOperationException($"Duplicate dynamic tool step argument '{key}'.");
        }

        return this with
        {
            Id = id,
            CapabilityId = capabilityId,
            Description = description,
            DependsOnStepIds = (DependsOnStepIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToArray(),
            Arguments = arguments,
            Condition = (Condition ?? new()).Normalize()
        };
    }
}

public sealed record SegaDynamicToolDefinition
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public SegaDynamicToolPersistence Persistence { get; init; } =
        SegaDynamicToolPersistence.Temporary;
    public SegaDynamicToolStatus Status { get; init; } = SegaDynamicToolStatus.Active;
    public IReadOnlyList<SegaDynamicToolParameterDefinition> Parameters { get; init; } =
        Array.Empty<SegaDynamicToolParameterDefinition>();
    public IReadOnlyList<SegaDynamicToolStep> Steps { get; init; } =
        Array.Empty<SegaDynamicToolStep>();
    public int TimeoutSeconds { get; init; } = 600;
    public bool AllowIndependentConcurrency { get; init; }
    public string CreationReason { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public SegaDynamicToolDefinition Normalize()
    {
        string name = Name?.Trim() ?? string.Empty;
        string description = Description?.Trim() ?? string.Empty;
        string creationReason = CreationReason?.Trim() ?? string.Empty;

        if (name.Length > 96 || description.Length > 1800 || creationReason.Length > 1600)
            throw new InvalidOperationException("Dynamic tool metadata is too long.");

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return this with
        {
            Name = name,
            Description = description,
            Version = Math.Max(1, Version),
            Parameters = (Parameters ?? Array.Empty<SegaDynamicToolParameterDefinition>())
                .Where(value => value != null)
                .Select(value => value.Normalize())
                .Take(16)
                .ToArray(),
            Steps = (Steps ?? Array.Empty<SegaDynamicToolStep>())
                .Where(value => value != null)
                .Select(value => value.Normalize())
                .Take(32)
                .ToArray(),
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 1800),
            CreationReason = creationReason,
            CreatedAtUtc = CreatedAtUtc == default ? now : CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc == default ? now : UpdatedAtUtc
        };
    }
}

public sealed record SegaDynamicToolProposal
{
    public SegaDynamicToolProposalAction Action { get; init; }
    public string? ToolId { get; init; }
    public SegaDynamicToolDefinition? Definition { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double Confidence { get; init; }

    public SegaDynamicToolProposal Normalize()
    {
        string reason = Reason?.Trim() ?? string.Empty;
        if (reason.Length > 1600)
            throw new InvalidOperationException("Dynamic tool proposal reason is too long.");

        return this with
        {
            ToolId = string.IsNullOrWhiteSpace(ToolId) ? null : ToolId.Trim(),
            Definition = Definition?.Normalize(),
            Reason = reason,
            Confidence = Math.Clamp(Confidence, 0.0, 1.0)
        };
    }

    public string BuildSignature()
    {
        SegaDynamicToolProposal normalized = Normalize();
        string definition = normalized.Definition == null
            ? string.Empty
            : JsonSerializer.Serialize(normalized.Definition);
        return string.Join('|',
            normalized.Action,
            normalized.ToolId ?? string.Empty,
            definition,
            normalized.Reason.ToLowerInvariant());
    }
}

public sealed record SegaDynamicToolInvocation
{
    public string ToolId { get; init; } = string.Empty;
    public JsonElement Arguments { get; init; }
    public string Reason { get; init; } = string.Empty;

    public SegaDynamicToolInvocation Normalize()
    {
        string toolId = ToolId?.Trim() ?? string.Empty;
        if (toolId.Length > 80)
            throw new InvalidOperationException("Dynamic tool invocation ID is too long.");

        JsonElement arguments;
        if (Arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            using JsonDocument document = JsonDocument.Parse("{}");
            arguments = document.RootElement.Clone();
        }
        else if (Arguments.ValueKind == JsonValueKind.Object)
        {
            arguments = Arguments.Clone();
        }
        else
        {
            throw new InvalidOperationException("Dynamic tool invocation arguments must be a JSON object.");
        }

        string reason = Reason?.Trim() ?? string.Empty;
        if (reason.Length > 1600)
            throw new InvalidOperationException("Dynamic tool invocation reason is too long.");

        return this with
        {
            ToolId = toolId,
            Arguments = arguments,
            Reason = reason
        };
    }

    public string BuildSignature()
    {
        SegaDynamicToolInvocation normalized = Normalize();
        StringBuilder builder = new();
        builder.Append(normalized.ToolId.ToLowerInvariant());
        foreach (JsonProperty property in normalized.Arguments.EnumerateObject()
                     .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|');
            builder.Append(property.Name.ToLowerInvariant());
            builder.Append('=');
            builder.Append(property.Value.GetRawText());
        }
        return builder.ToString();
    }
}

public sealed record SegaDynamicToolRecord
{
    public required SegaDynamicToolDefinition Definition { get; init; }
    public int ExecutionCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public string LastResult { get; init; } = string.Empty;
    public DateTimeOffset? LastExecutedAtUtc { get; init; }

    public double Reliability => ExecutionCount <= 0
        ? 0.0
        : Math.Clamp((double)SuccessCount / ExecutionCount, 0.0, 1.0);
}


public sealed record SegaDynamicToolExecutionHistoryRecord
{
    public Guid ExecutionId { get; init; }
    public Guid ToolId { get; init; }
    public int ToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public bool Aborted { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record SegaDynamicToolMutationResult
{
    public SegaDynamicToolApplyAction Action { get; init; }
    public SegaDynamicToolDefinition? Tool { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool Changed => Action is
        SegaDynamicToolApplyAction.Created or
        SegaDynamicToolApplyAction.Revised or
        SegaDynamicToolApplyAction.Enabled or
        SegaDynamicToolApplyAction.Disabled or
        SegaDynamicToolApplyAction.Retired;
}

public sealed record SegaDynamicToolStepExecutionResult
{
    public string StepId { get; init; } = string.Empty;
    public SegaDynamicToolStepStatus Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public SegaCapabilityRequest? Request { get; init; }
    public SegaCapabilityResult? CapabilityResult { get; init; }
}

public sealed record SegaDynamicToolExecutionResult
{
    public Guid ToolId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public int ToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public bool Aborted { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<SegaDynamicToolStepExecutionResult> StepResults { get; init; } =
        Array.Empty<SegaDynamicToolStepExecutionResult>();
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }

    public IReadOnlyList<SegaCapabilityResult> CapabilityResults =>
        StepResults
            .Select(value => value.CapabilityResult)
            .Where(value => value != null)
            .Cast<SegaCapabilityResult>()
            .ToArray();
}
