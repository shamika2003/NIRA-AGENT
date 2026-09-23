/*
 * filename: NIRADynamicToolContracts.cs
 */

using System.Text;
using System.Text.Json;

using NIRAAgent.Capabilities;

namespace NIRAAgent.Tools;

// =============================================================
// DYNAMIC TOOL MODEL
//
// A dynamic tool is a validated, parameterized composition of
// trusted primitive capability calls. It is not executable C# and
// it never grants authority by itself.
// =============================================================

public enum NIRADynamicToolPersistence
{
    Temporary,
    Persistent
}

public enum NIRADynamicToolStatus
{
    Active,
    Disabled,
    Retired
}

public enum NIRADynamicToolProposalAction
{
    Create,
    Revise,
    Enable,
    Disable,
    Retire
}

public enum NIRADynamicToolApplyAction
{
    Rejected,
    Created,
    Revised,
    Enabled,
    Disabled,
    Retired,
    NoChange
}

public enum NIRADynamicToolFailurePolicy
{
    StopTool,
    Continue
}

public enum NIRADynamicValueSource
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

public enum NIRADynamicConditionKind
{
    Always,
    ParameterEquals,
    StepSucceeded,
    StepFailed
}

public enum NIRADynamicToolStepStatus
{
    Succeeded,
    Failed,
    Skipped
}

public sealed record NIRADynamicToolParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public bool Required { get; init; } = true;
    public JsonElement? DefaultValue { get; init; }
    public string Description { get; init; } = string.Empty;

    public NIRADynamicToolParameterDefinition Normalize()
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

public sealed record NIRADynamicToolValueBinding
{
    public NIRADynamicValueSource Source { get; init; } = NIRADynamicValueSource.Literal;
    public JsonElement? Literal { get; init; }
    public string? Template { get; init; }
    public string? ParameterName { get; init; }
    public string? StepId { get; init; }

    public NIRADynamicToolValueBinding Normalize()
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

public sealed record NIRADynamicToolCondition
{
    public NIRADynamicConditionKind Kind { get; init; } = NIRADynamicConditionKind.Always;
    public string? ParameterName { get; init; }
    public string? StepId { get; init; }
    public JsonElement? ExpectedValue { get; init; }

    public NIRADynamicToolCondition Normalize()
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

public sealed record NIRADynamicToolStep
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnStepIds { get; init; } = Array.Empty<string>();
    public Dictionary<string, NIRADynamicToolValueBinding> Arguments { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public NIRADynamicToolCondition Condition { get; init; } = new();
    public NIRADynamicToolFailurePolicy FailurePolicy { get; init; } =
        NIRADynamicToolFailurePolicy.StopTool;

    public NIRADynamicToolStep Normalize()
    {
        string id = Id?.Trim() ?? string.Empty;
        string capabilityId = CapabilityId?.Trim().ToLowerInvariant() ?? string.Empty;
        string description = Description?.Trim() ?? string.Empty;

        if (id.Length > 64 || capabilityId.Length > 160 || description.Length > 800)
            throw new InvalidOperationException("Dynamic tool step metadata is too long.");

        Dictionary<string, NIRADynamicToolValueBinding> arguments =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, NIRADynamicToolValueBinding> entry
                 in Arguments ?? new Dictionary<string, NIRADynamicToolValueBinding>())
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

public sealed record NIRADynamicToolDefinition
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public NIRADynamicToolPersistence Persistence { get; init; } =
        NIRADynamicToolPersistence.Temporary;
    public NIRADynamicToolStatus Status { get; init; } = NIRADynamicToolStatus.Active;
    public IReadOnlyList<NIRADynamicToolParameterDefinition> Parameters { get; init; } =
        Array.Empty<NIRADynamicToolParameterDefinition>();
    public IReadOnlyList<NIRADynamicToolStep> Steps { get; init; } =
        Array.Empty<NIRADynamicToolStep>();
    public int TimeoutSeconds { get; init; } = 600;
    public bool AllowIndependentConcurrency { get; init; }
    public string CreationReason { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public NIRADynamicToolDefinition Normalize()
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
            Parameters = (Parameters ?? Array.Empty<NIRADynamicToolParameterDefinition>())
                .Where(value => value != null)
                .Select(value => value.Normalize())
                .Take(16)
                .ToArray(),
            Steps = (Steps ?? Array.Empty<NIRADynamicToolStep>())
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

public sealed record NIRADynamicToolProposal
{
    public NIRADynamicToolProposalAction Action { get; init; }
    public string? ToolId { get; init; }
    public NIRADynamicToolDefinition? Definition { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double Confidence { get; init; }
    // Optional one-call bounded execution: only a validated newly CREATED
    // Temporary tool may be invoked this cycle, with its committed GUID.
    public bool RunAfterCreate { get; init; }
    public JsonElement InvocationArguments { get; init; }

    public NIRADynamicToolProposal Normalize()
    {
        string reason = Reason?.Trim() ?? string.Empty;
        if (reason.Length > 1600)
            throw new InvalidOperationException("Dynamic tool proposal reason is too long.");

        JsonElement invocationArguments;
        if (InvocationArguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            using JsonDocument empty = JsonDocument.Parse("{}");
            invocationArguments = empty.RootElement.Clone();
        }
        else if (InvocationArguments.ValueKind == JsonValueKind.Object)
            invocationArguments = InvocationArguments.Clone();
        else
            throw new InvalidOperationException(
                "Dynamic tool invocationArguments must be a JSON object.");

        return this with
        {
            ToolId = string.IsNullOrWhiteSpace(ToolId) ? null : ToolId.Trim(),
            Definition = Definition?.Normalize(),
            Reason = reason,
            Confidence = Math.Clamp(Confidence, 0.0, 1.0),
            RunAfterCreate = Action == NIRADynamicToolProposalAction.Create && RunAfterCreate,
            InvocationArguments = invocationArguments
        };
    }

    public string BuildSignature()
    {
        NIRADynamicToolProposal normalized = Normalize();
        string definition = normalized.Definition == null
            ? string.Empty
            : JsonSerializer.Serialize(normalized.Definition);
        return string.Join('|',
            normalized.Action,
            normalized.ToolId ?? string.Empty,
            definition,
            normalized.Reason.ToLowerInvariant(),
            normalized.RunAfterCreate,
            normalized.RunAfterCreate
                ? normalized.InvocationArguments.GetRawText() : string.Empty);
    }
}

public sealed record NIRADynamicToolInvocation
{
    public string ToolId { get; init; } = string.Empty;
    public string? SourceSkillId { get; init; }
    public JsonElement Arguments { get; init; }
    public string Reason { get; init; } = string.Empty;

    public NIRADynamicToolInvocation Normalize()
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

        string? sourceSkillId = string.IsNullOrWhiteSpace(SourceSkillId)
            ? null
            : SourceSkillId.Trim();
        if (sourceSkillId?.Length > 80)
            throw new InvalidOperationException("Dynamic tool invocation source skill ID is too long.");

        string reason = Reason?.Trim() ?? string.Empty;
        if (reason.Length > 1600)
            throw new InvalidOperationException("Dynamic tool invocation reason is too long.");

        return this with
        {
            ToolId = toolId,
            SourceSkillId = sourceSkillId,
            Arguments = arguments,
            Reason = reason
        };
    }

    public string BuildSignature()
    {
        NIRADynamicToolInvocation normalized = Normalize();
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

public sealed record NIRADynamicToolRecord
{
    public required NIRADynamicToolDefinition Definition { get; init; }
    public int ExecutionCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public string LastResult { get; init; } = string.Empty;
    public DateTimeOffset? LastExecutedAtUtc { get; init; }

    public double Reliability => ExecutionCount <= 0
        ? 0.0
        : Math.Clamp((double)SuccessCount / ExecutionCount, 0.0, 1.0);
}


public sealed record NIRADynamicToolExecutionHistoryRecord
{
    public Guid ExecutionId { get; init; }
    public Guid ToolId { get; init; }
    public int ToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public bool Aborted { get; init; }
    // One-way SHA-256 fingerprint of the normalized invocation arguments.
    // Raw arguments are intentionally not persisted here; Stage 13 only needs
    // to know whether a parameterized procedure has succeeded across distinct
    // invocation contexts.
    public string InvocationFingerprint { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record NIRADynamicToolMutationResult
{
    public NIRADynamicToolApplyAction Action { get; init; }
    public NIRADynamicToolDefinition? Tool { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool Changed => Action is
        NIRADynamicToolApplyAction.Created or
        NIRADynamicToolApplyAction.Revised or
        NIRADynamicToolApplyAction.Enabled or
        NIRADynamicToolApplyAction.Disabled or
        NIRADynamicToolApplyAction.Retired;
}

public sealed record NIRADynamicToolStepExecutionResult
{
    public string StepId { get; init; } = string.Empty;
    public NIRADynamicToolStepStatus Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public NIRACapabilityRequest? Request { get; init; }
    public NIRACapabilityResult? CapabilityResult { get; init; }
}

public sealed record NIRADynamicToolExecutionResult
{
    public Guid ToolId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public int ToolVersion { get; init; }
    public bool Succeeded { get; init; }
    public bool Aborted { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<NIRADynamicToolStepExecutionResult> StepResults { get; init; } =
        Array.Empty<NIRADynamicToolStepExecutionResult>();
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }

    public IReadOnlyList<NIRACapabilityResult> CapabilityResults =>
        StepResults
            .Select(value => value.CapabilityResult)
            .Where(value => value != null)
            .Cast<NIRACapabilityResult>()
            .ToArray();
}
