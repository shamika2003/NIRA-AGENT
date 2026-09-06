/*
 * filename: SegaDynamicToolValidator.cs
 */

using System.Text.Json;
using System.Text.RegularExpressions;

using SegaAgent.Capabilities;
using SegaAgent.Memory.LongTerm;

namespace SegaAgent.Tools;

public sealed class SegaDynamicToolValidator
{
    private static readonly Regex NameRegex =
        new(@"^[A-Za-z][A-Za-z0-9 _.-]{1,95}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SymbolRegex =
        new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TemplateTokenRegex =
        new(
            @"\$\{(?<kind>param|step):(?<name>[A-Za-z][A-Za-z0-9_]{0,63})(?:\.(?<field>output|summary|exitCode|httpStatusCode|succeeded))?\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly SegaCapabilityRegistry _registry;

    public SegaDynamicToolValidator(SegaCapabilityRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public SegaDynamicToolDefinition ValidateDefinition(SegaDynamicToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        SegaDynamicToolDefinition normalized = definition.Normalize();

        if (normalized.Id == Guid.Empty)
            throw new InvalidOperationException("A committed dynamic tool requires an ID.");
        if (!NameRegex.IsMatch(normalized.Name))
            throw new InvalidOperationException("Dynamic tool name must start with a letter and use only letters, numbers, spaces, '.', '_' or '-'.");
        if (normalized.Steps.Count == 0)
            throw new InvalidOperationException("A dynamic tool requires at least one primitive step.");
        if (normalized.Steps.Count > 32)
            throw new InvalidOperationException("A dynamic tool may contain at most 32 primitive steps.");

        if (SegaSensitiveMemoryPolicy.ContainsSensitiveSecret(JsonSerializer.Serialize(normalized)))
            throw new InvalidOperationException("Dynamic tool definitions may not persist credential/private-key material. Supply sensitive values at invocation time instead.");

        Dictionary<string, SegaDynamicToolParameterDefinition> parameters =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (SegaDynamicToolParameterDefinition parameter in normalized.Parameters)
        {
            if (!SymbolRegex.IsMatch(parameter.Name))
                throw new InvalidOperationException($"Invalid dynamic tool parameter name '{parameter.Name}'.");
            if (!IsSupportedType(parameter.Type))
                throw new InvalidOperationException($"Unsupported dynamic tool parameter type '{parameter.Type}'.");
            if (!parameters.TryAdd(parameter.Name, parameter))
                throw new InvalidOperationException($"Duplicate dynamic tool parameter '{parameter.Name}'.");
            if (parameter.DefaultValue.HasValue &&
                !IsCompatible(parameter.DefaultValue.Value, parameter.Type))
                throw new InvalidOperationException($"Default value for '{parameter.Name}' does not match {parameter.Type}.");
        }

        Dictionary<string, SegaDynamicToolStep> steps =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (SegaDynamicToolStep step in normalized.Steps)
        {
            if (!SymbolRegex.IsMatch(step.Id))
                throw new InvalidOperationException($"Invalid dynamic tool step ID '{step.Id}'.");
            if (!steps.TryAdd(step.Id, step))
                throw new InvalidOperationException($"Duplicate dynamic tool step ID '{step.Id}'.");
        }

        foreach (SegaDynamicToolStep step in normalized.Steps)
        {
            if (!_registry.TryResolve(step.CapabilityId, out ISegaCapabilityHandler? handler) || handler == null)
                throw new InvalidOperationException($"Dynamic tool step '{step.Id}' references unknown primitive '{step.CapabilityId}'.");

            SegaCapabilityDescriptor descriptor = handler.Descriptor.Normalize();

            foreach (string dependency in step.DependsOnStepIds)
            {
                if (!steps.ContainsKey(dependency))
                    throw new InvalidOperationException($"Step '{step.Id}' depends on unknown step '{dependency}'.");
                if (string.Equals(dependency, step.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Step '{step.Id}' cannot depend on itself.");
            }

            Dictionary<string, SegaCapabilityParameterDescriptor> capabilityParameters =
                descriptor.Parameters.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);

            foreach (string argumentName in step.Arguments.Keys)
            {
                if (!capabilityParameters.ContainsKey(argumentName))
                    throw new InvalidOperationException($"Step '{step.Id}' supplies unknown argument '{argumentName}' for primitive '{descriptor.Id}'.");
            }

            foreach (SegaCapabilityParameterDescriptor capabilityParameter in descriptor.Parameters)
            {
                if (!step.Arguments.TryGetValue(capabilityParameter.Name, out SegaDynamicToolValueBinding? binding))
                {
                    if (capabilityParameter.Required)
                        throw new InvalidOperationException($"Step '{step.Id}' is missing required primitive argument '{capabilityParameter.Name}'.");
                    continue;
                }

                ValidateBinding(step, binding, capabilityParameter, parameters, steps);
            }

            ValidateCondition(step, parameters, steps);
        }

        _ = GetExecutionOrder(normalized);
        return normalized;
    }

    public IReadOnlyDictionary<string, JsonElement> ResolveInvocationArguments(
        SegaDynamicToolDefinition definition,
        SegaDynamicToolInvocation invocation)
    {
        SegaDynamicToolDefinition normalized = ValidateDefinition(definition);
        SegaDynamicToolInvocation clean = invocation.Normalize();

        Dictionary<string, SegaDynamicToolParameterDefinition> definitions =
            normalized.Parameters.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> supplied = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in clean.Arguments.EnumerateObject())
        {
            if (!definitions.TryGetValue(property.Name, out SegaDynamicToolParameterDefinition? parameter))
                throw new InvalidOperationException($"Unknown argument '{property.Name}' for dynamic tool '{normalized.Name}'.");
            if (!IsCompatible(property.Value, parameter.Type))
                throw new InvalidOperationException($"Argument '{property.Name}' must be {parameter.Type}.");
            supplied[property.Name] = property.Value.Clone();
        }

        foreach (SegaDynamicToolParameterDefinition parameter in normalized.Parameters)
        {
            if (supplied.ContainsKey(parameter.Name)) continue;
            if (parameter.DefaultValue.HasValue)
            {
                supplied[parameter.Name] = parameter.DefaultValue.Value.Clone();
                continue;
            }
            if (parameter.Required)
                throw new InvalidOperationException($"Required dynamic tool argument '{parameter.Name}' is missing.");
        }

        return supplied;
    }

    public IReadOnlyList<SegaDynamicToolStep> GetExecutionOrder(SegaDynamicToolDefinition definition)
    {
        SegaDynamicToolDefinition normalized = definition.Normalize();
        Dictionary<string, SegaDynamicToolStep> byId =
            normalized.Steps.ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> indegree = byId.Keys.ToDictionary(value => value, _ => 0, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> children = byId.Keys.ToDictionary(value => value, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (SegaDynamicToolStep step in normalized.Steps)
        {
            foreach (string dependency in step.DependsOnStepIds)
            {
                if (!byId.ContainsKey(dependency))
                    throw new InvalidOperationException($"Step '{step.Id}' depends on unknown step '{dependency}'.");
                indegree[step.Id]++;
                children[dependency].Add(step.Id);
            }
        }

        Dictionary<string, int> originalOrder = normalized.Steps
            .Select((value, index) => (value.Id, index))
            .ToDictionary(value => value.Id, value => value.index, StringComparer.OrdinalIgnoreCase);

        List<string> ready = indegree
            .Where(value => value.Value == 0)
            .Select(value => value.Key)
            .OrderBy(value => originalOrder[value])
            .ToList();

        List<SegaDynamicToolStep> result = new();
        while (ready.Count > 0)
        {
            string id = ready[0];
            ready.RemoveAt(0);
            result.Add(byId[id]);

            foreach (string child in children[id])
            {
                indegree[child]--;
                if (indegree[child] == 0)
                {
                    ready.Add(child);
                    ready.Sort((left, right) => originalOrder[left].CompareTo(originalOrder[right]));
                }
            }
        }

        if (result.Count != normalized.Steps.Count)
            throw new InvalidOperationException("Dynamic tool step dependencies contain a cycle.");

        return result;
    }

    private static void ValidateBinding(
        SegaDynamicToolStep step,
        SegaDynamicToolValueBinding binding,
        SegaCapabilityParameterDescriptor target,
        IReadOnlyDictionary<string, SegaDynamicToolParameterDefinition> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStep> steps)
    {
        switch (binding.Source)
        {
            case SegaDynamicValueSource.Literal:
                if (!binding.Literal.HasValue)
                {
                    if (target.Required)
                        throw new InvalidOperationException($"Step '{step.Id}' required argument '{target.Name}' has an empty literal binding.");
                    return;
                }
                if (!IsCompatible(binding.Literal.Value, target.Type))
                    throw new InvalidOperationException($"Step '{step.Id}' literal for '{target.Name}' must be {target.Type}.");
                return;

            case SegaDynamicValueSource.Template:
                ValidateTemplateBinding(step, binding, target, parameters, steps);
                return;

            case SegaDynamicValueSource.Parameter:
                if (binding.ParameterName == null || !parameters.TryGetValue(binding.ParameterName, out SegaDynamicToolParameterDefinition? parameter))
                    throw new InvalidOperationException($"Step '{step.Id}' references an unknown tool parameter.");
                if (!string.Equals(parameter.Type, target.Type, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Step '{step.Id}' parameter '{parameter.Name}' type does not match primitive argument '{target.Name}'.");
                if (target.Required && !parameter.Required && !parameter.DefaultValue.HasValue)
                    throw new InvalidOperationException($"Step '{step.Id}' required primitive argument '{target.Name}' cannot bind to optional parameter '{parameter.Name}' without a default.");
                return;

            case SegaDynamicValueSource.StepOutput:
            case SegaDynamicValueSource.StepSummary:
                RequireStepDependency(step, binding.StepId, steps);
                RequireTargetType(step, target, "string");
                return;

            case SegaDynamicValueSource.StepExitCode:
            case SegaDynamicValueSource.StepHttpStatusCode:
                RequireStepDependency(step, binding.StepId, steps);
                RequireTargetType(step, target, "integer");
                return;

            case SegaDynamicValueSource.StepSucceeded:
                RequireStepDependency(step, binding.StepId, steps);
                RequireTargetType(step, target, "boolean");
                return;

            default:
                throw new InvalidOperationException($"Step '{step.Id}' contains an unsupported value binding.");
        }
    }


    private static void ValidateTemplateBinding(
        SegaDynamicToolStep step,
        SegaDynamicToolValueBinding binding,
        SegaCapabilityParameterDescriptor target,
        IReadOnlyDictionary<string, SegaDynamicToolParameterDefinition> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStep> steps)
    {
        if (!string.Equals(target.Type, "string", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Step '{step.Id}' template binding can only target string primitive arguments; '{target.Name}' expects {target.Type}.");

        if (string.IsNullOrWhiteSpace(binding.Template))
            throw new InvalidOperationException(
                $"Step '{step.Id}' template binding for '{target.Name}' is empty.");

        string template = binding.Template;
        MatchCollection matches = TemplateTokenRegex.Matches(template);

        string stripped = TemplateTokenRegex.Replace(template, string.Empty);
        if (stripped.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Step '{step.Id}' template binding for '{target.Name}' contains a malformed placeholder.");
        }

        foreach (Match match in matches)
        {
            string kind = match.Groups["kind"].Value;
            string name = match.Groups["name"].Value;
            string field = match.Groups["field"].Value;

            if (kind.Equals("param", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(field))
                    throw new InvalidOperationException(
                        $"Step '{step.Id}' parameter template placeholder for '{name}' cannot specify a step field.");

                if (!parameters.ContainsKey(name))
                    throw new InvalidOperationException(
                        $"Step '{step.Id}' template references unknown tool parameter '{name}'.");

                continue;
            }

            if (kind.Equals("step", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(field))
                    throw new InvalidOperationException(
                        $"Step '{step.Id}' step template placeholder for '{name}' requires one of output, summary, exitCode, httpStatusCode, or succeeded.");

                RequireStepDependency(step, name, steps);
                continue;
            }

            throw new InvalidOperationException(
                $"Step '{step.Id}' contains an unsupported template placeholder.");
        }
    }

    private static void ValidateCondition(
        SegaDynamicToolStep step,
        IReadOnlyDictionary<string, SegaDynamicToolParameterDefinition> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStep> steps)
    {
        SegaDynamicToolCondition condition = step.Condition;
        switch (condition.Kind)
        {
            case SegaDynamicConditionKind.Always:
                return;

            case SegaDynamicConditionKind.ParameterEquals:
                if (condition.ParameterName == null ||
                    !parameters.TryGetValue(condition.ParameterName, out SegaDynamicToolParameterDefinition? parameter) ||
                    !condition.ExpectedValue.HasValue)
                    throw new InvalidOperationException($"Step '{step.Id}' has an invalid ParameterEquals condition.");
                if (!IsCompatible(condition.ExpectedValue.Value, parameter.Type))
                    throw new InvalidOperationException($"Step '{step.Id}' condition value does not match parameter '{parameter.Name}'.");
                return;

            case SegaDynamicConditionKind.StepSucceeded:
            case SegaDynamicConditionKind.StepFailed:
                RequireStepDependency(step, condition.StepId, steps);
                return;

            default:
                throw new InvalidOperationException($"Step '{step.Id}' has an unsupported condition.");
        }
    }

    private static void RequireStepDependency(
        SegaDynamicToolStep step,
        string? referencedStepId,
        IReadOnlyDictionary<string, SegaDynamicToolStep> steps)
    {
        if (string.IsNullOrWhiteSpace(referencedStepId) || !steps.ContainsKey(referencedStepId))
            throw new InvalidOperationException($"Step '{step.Id}' references an unknown earlier step.");
        if (!step.DependsOnStepIds.Contains(referencedStepId, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Step '{step.Id}' must declare '{referencedStepId}' as a dependency before using its result.");
    }

    private static void RequireTargetType(
        SegaDynamicToolStep step,
        SegaCapabilityParameterDescriptor target,
        string expected)
    {
        if (!string.Equals(target.Type, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Step '{step.Id}' cannot bind this step-result value to '{target.Name}' because the primitive expects {target.Type}.");
    }

    private static bool IsSupportedType(string type) =>
        type.ToLowerInvariant() is "string" or "boolean" or "integer" or "object";

    internal static bool IsCompatible(JsonElement value, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => false
        };
    }
}
