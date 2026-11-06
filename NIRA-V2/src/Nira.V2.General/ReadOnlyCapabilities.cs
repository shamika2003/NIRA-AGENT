using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.General;

/// <summary>Deterministic arithmetic; never evaluates code or starts a process.</summary>
public sealed class CalculatorCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    public CalculatorCapability(AgentPayloadVault vault) => _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    public AgentToolSpec Spec { get; } = new("math.calculate",
        "Calculate a decimal arithmetic expression using +, -, *, / and parentheses. Use for numeric precision, not as a programming shell.", AccessLevel.Observe);
    public string FunctionName => "math_calculate";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
        type = "object", properties = new { expression = new { type = "string",
            description = "Decimal arithmetic, 1-200 characters; + - * / ( ) only" } },
        required = new[] { "expression" }, additionalProperties = false });

    public bool ValidateArguments(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.EnumerateObject().Count() == 1 &&
        node.TryGetProperty("expression", out JsonElement expr) &&
        expr.ValueKind == JsonValueKind.String && expr.GetString() is string s &&
        s.Trim().Length is >= 1 and <= 200 && s.All(c =>
            char.IsAsciiDigit(c) || c is '+' or '-' or '*' or '/' or '(' or ')' or '.' or ' ');

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        JsonElement args;
        try {
            runId = _vault.RunIdFromArgument(reference);
            args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException)
        { return new AgentToolReceipt(false, false, null, "INVALID_SCOPED_ARGUMENT"); }
        if (!ValidateArguments(args)) return new AgentToolReceipt(false, false, null, "INVALID_MATH_ARGUMENTS");
        string expression = args.GetProperty("expression").GetString()!;
        if (!DecimalExpression.TryEvaluate(expression, out decimal result))
            return new AgentToolReceipt(false, false, null, "ARITHMETIC_INVALID_OR_OVERFLOW");
        string answer = result.ToString("G29", CultureInfo.InvariantCulture);
        JsonElement output = JsonSerializer.SerializeToElement(new { expression, result = answer });
        string token = await _vault.SaveResultAsync(runId, Spec.Id, output, ct);
        return AgentToolReceipt.ReadOnlyResult(token);
    }
}

public sealed class PlatformCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    public PlatformCapability(AgentPayloadVault vault) => _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    public AgentToolSpec Spec { get; } = new("system.platform",
        "Observe non-identifying runtime platform details (OS family, CPU architecture, .NET version). No files or processes are accessed.", AccessLevel.Observe);
    public string FunctionName => "system_platform";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false });
    public bool ValidateArguments(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && !node.EnumerateObject().Any();
    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        JsonElement arguments;
        try {
            runId = _vault.RunIdFromArgument(reference);
            arguments = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException)
        { return new AgentToolReceipt(false, false, null, "INVALID_SCOPED_ARGUMENT"); }
        if (!ValidateArguments(arguments))
            return new AgentToolReceipt(false, false, null, "INVALID_PLATFORM_ARGUMENTS");
        JsonElement result = JsonSerializer.SerializeToElement(new {
            osFamily = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" :
                OperatingSystem.IsMacOS() ? "macOS" : "Other",
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription
        });
        return AgentToolReceipt.ReadOnlyResult(await _vault.SaveResultAsync(runId, Spec.Id, result, ct));
    }
}

internal sealed class DecimalExpression
{
    private readonly string _text;
    private int _at;
    private int _steps;
    private DecimalExpression(string text) => _text = text;
    public static bool TryEvaluate(string text, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200 ||
            text.Any(c => !(char.IsAsciiDigit(c) || c is '+' or '-' or '*' or '/' or '(' or ')' or '.' or ' ')))
            return false;
        try {
            var parser = new DecimalExpression(text);
            decimal value = parser.AddSub();
            parser.Skip();
            if (parser._at != text.Length) return false;
            result = value;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or DivideByZeroException)
        { return false; }
    }
    private void Skip() { while (_at < _text.Length && _text[_at] == ' ') _at++; }
    private bool Consume(char c) { Skip(); if (_at >= _text.Length || _text[_at] != c) return false; _at++; return true; }
    private void Tick() { if (++_steps > 240) throw new FormatException("Expression complexity exceeded."); }
    private decimal AddSub()
    {
        Tick(); decimal current = MulDiv();
        while (true) {
            if (Consume('+')) current = checked(current + MulDiv());
            else if (Consume('-')) current = checked(current - MulDiv());
            else return current;
        }
    }
    private decimal MulDiv()
    {
        Tick(); decimal current = Atom();
        while (true) {
            if (Consume('*')) current = checked(current * Atom());
            else if (Consume('/')) current = checked(current / Atom());
            else return current;
        }
    }
    private decimal Atom()
    {
        Tick();
        if (Consume('+')) return Atom();
        if (Consume('-')) return checked(-Atom());
        if (Consume('(')) {
            decimal result = AddSub();
            if (!Consume(')')) throw new FormatException("Missing closing parenthesis.");
            return result;
        }
        Skip(); int start = _at;
        while (_at < _text.Length && (char.IsAsciiDigit(_text[_at]) || _text[_at] == '.')) _at++;
        if (_at == start || !decimal.TryParse(_text.AsSpan(start, _at - start),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            throw new FormatException("Invalid number.");
        return value;
    }
}
