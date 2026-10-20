using System.Globalization;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

// A pure, bounded decimal calculator. Expressions are parsed as arithmetic,
// NEVER executed as C#, JavaScript, PowerShell or dynamically compiled code.
// No problem-specific formulas, financial assumptions or saved-answer tables.
public sealed class NIRAMathCalculationCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.MathCalculate,
        Description = "Deterministically calculate arithmetic from an explicit expression. Supports decimal/scientific numbers, + - * / ^, parentheses, postfix percent (9% = 0.09), sqrt/abs/min/max/round, variables and optional ordered named steps. Use for multi-step mathematics, finance, rates and unit-converted arithmetic instead of mental computation. Integer exponents only; .NET decimal has approximately 28-29 significant digits. Read-only, no code execution, no external data. Always derive the expression and units from the user's actual problem; verify the expression represents the requested situation.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[]
        {
            new NIRACapabilityParameterDescriptor
            {
                Name = "expression", Type = "string", Required = true,
                Description = "Final arithmetic expression, e.g. (a+b)/c, or last named step. Use ^ for integer powers and 9% for 0.09. No variable assignment or programming code."
            },
            new NIRACapabilityParameterDescriptor
            {
                Name = "variables", Type = "object", Required = false,
                Description = "Optional variable-name to finite decimal-number mapping, e.g. {\"x\":12.5,\"y\":4}."
            },
            new NIRACapabilityParameterDescriptor
            {
                Name = "steps", Type = "array", Required = false,
                Description = "Optional ordered intermediate steps: [{\"name\":\"first\",\"expression\":\"...\"}]. Later steps and final expression can use prior step names WITHOUT displayed rounding. Maximum 16."
            },
            new NIRACapabilityParameterDescriptor
            {
                Name = "displayDecimals", Type = "integer", Required = false,
                Description = "Final-display decimal places (0..12, default 6); underlying step values are never rounded to this setting. For money often use 2."
            }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest _) => NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string expression = NIRACapabilityArguments.RequireString(request, "expression", 1500);
        int displayDecimals = NIRACapabilityArguments.GetInteger(request, "displayDecimals", 6, 0, 12);
        if (request.Arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Math calculation arguments must be a JSON object.");

        var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (request.Arguments.TryGetProperty("variables", out JsonElement variableElement) &&
            variableElement.ValueKind != JsonValueKind.Null)
        {
            if (variableElement.ValueKind != JsonValueKind.Object || variableElement.EnumerateObject().Count() > 24)
                throw new InvalidOperationException("variables must be an object with at most 24 numeric entries.");
            foreach (JsonProperty property in variableElement.EnumerateObject())
            {
                NIRADecimalExpressionEvaluator.ValidateName(property.Name);
                if (variables.ContainsKey(property.Name))
                    throw new InvalidOperationException("Duplicate variable name: " + property.Name);
                variables.Add(property.Name, ParseDecimal(property.Value));
            }
        }

        var results = new List<MathResult>();
        int budget = expression.Length;
        if (request.Arguments.TryGetProperty("steps", out JsonElement stepsElement) &&
            stepsElement.ValueKind != JsonValueKind.Null)
        {
            if (stepsElement.ValueKind != JsonValueKind.Array || stepsElement.GetArrayLength() > 16)
                throw new InvalidOperationException("steps must be an array of at most 16 named expressions.");
            foreach (JsonElement step in stepsElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (step.ValueKind != JsonValueKind.Object ||
                    !step.TryGetProperty("name", out JsonElement nameValue) ||
                    !step.TryGetProperty("expression", out JsonElement formulaValue) ||
                    nameValue.ValueKind != JsonValueKind.String ||
                    formulaValue.ValueKind != JsonValueKind.String ||
                    step.EnumerateObject().Count() != 2)
                    throw new InvalidOperationException("Each step requires only name and expression strings.");
                string name = nameValue.GetString() ?? "";
                string formula = formulaValue.GetString() ?? "";
                NIRADecimalExpressionEvaluator.ValidateName(name);
                if (formula.Length is < 1 or > 1500 || variables.ContainsKey(name))
                    throw new InvalidOperationException("Empty/oversized step expression or duplicate step name: " + name);
                budget += formula.Length;
                if (budget > 8000)
                    throw new InvalidOperationException("Total expression length exceeds 8000 characters.");
                decimal value = NIRADecimalExpressionEvaluator.Calculate(formula, variables, cancellationToken);
                variables.Add(name, value);  // Full decimal value, NEVER the display-rounded value.
                results.Add(new MathResult(name, formula, FormatExact(value),
                    FormatDisplay(value, displayDecimals)));
            }
        }
        decimal finalValue = NIRADecimalExpressionEvaluator.Calculate(expression, variables, cancellationToken);
        var payload = new
        {
            engine = "bounded-decimal-expression-v1",
            numericSystem = ".NET decimal (~28-29 significant digits; divisions/square roots may round internally)",
            displayRounding = "AwayFromZero, at requested displayDecimals only; named steps retain unrounded decimal values",
            displayDecimals,
            steps = results,
            final = new MathResult("result", expression, FormatExact(finalValue),
                FormatDisplay(finalValue, displayDecimals)),
            units = "Not inferred by calculator; apply user-grounded units in the answer",
            externalStateAccess = false
        };
        return Task.FromResult(new NIRACapabilityHandlerResult
        {
            Summary = $"Calculated {results.Count} intermediate step(s). Final = {FormatDisplay(finalValue, displayDecimals)}; unrounded decimal value = {FormatExact(finalValue)}. Read-only calculator; units not inferred.",
            Output = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            ChangedSystemState = false
        });
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        string input = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString() ?? "",
            _ => throw new InvalidOperationException("Variable values must be decimal numbers or numeric strings.")
        };
        return NIRADecimalExpressionEvaluator.ParseFiniteDecimal(input);
    }

    private static string FormatExact(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static string FormatDisplay(decimal value, int places) =>
        decimal.Round(value, places, MidpointRounding.AwayFromZero)
            .ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private sealed record MathResult(string Name, string Expression, string Value, string DisplayValue);
}

// Explicit arithmetic grammar (no reflection, scripting, network, file I/O,
// unbounded loops or user-defined functions). The value of a named step is
// carried forward WITHOUT formatting/rounding and is not hardcoded to a test.
public static class NIRADecimalExpressionEvaluator
{
    public static decimal Calculate(string expression,
        IReadOnlyDictionary<string, decimal>? variables = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 1500)
            throw new InvalidOperationException("Expression must contain 1..1500 characters.");
        try
        {
            var parser = new Parser(expression, variables, cancellationToken);
            return parser.Evaluate();
        }
        catch (DivideByZeroException)
        {
            throw new InvalidOperationException("Division by zero in calculation.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Calculation exceeds .NET decimal's finite numeric range.");
        }
    }

    internal static decimal ParseFiniteDecimal(string input)
    {
        if (!decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture,
                out decimal value))
            throw new InvalidOperationException("Invalid or out-of-range decimal number.");
        int exponent = input.IndexOfAny(new[] { 'e', 'E' });
        string significand = exponent < 0 ? input : input[..exponent];
        if (value == 0m && significand.Any(ch => ch is >= '1' and <= '9'))
            throw new InvalidOperationException("Nonzero number is smaller than .NET decimal's minimum precision; cannot silently underflow to zero.");
        return value;
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 40 ||
            !(name[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_') ||
            name.Any(ch => !(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new InvalidOperationException("Variable names must be 1..40 ASCII letters/digits/underscores, starting with a letter or underscore.");
    }

    private sealed class Parser
    {
        private readonly string _source;
        private readonly IReadOnlyDictionary<string, decimal> _variables;
        private readonly CancellationToken _token;
        private int _index;
        private int _operations;
        private int _depth;

        public Parser(string source, IReadOnlyDictionary<string, decimal>? variables, CancellationToken token)
        {
            _source = source;
            _variables = variables ?? new Dictionary<string, decimal>();
            _token = token;
        }

        public decimal Evaluate()
        {
            decimal result = Sum();
            SkipWhitespace();
            if (_index != _source.Length)
                throw Error("Unexpected token");
            return result;
        }

        private decimal Sum()
        {
            Enter();
            try
            {
                decimal result = Product();
                while (true)
                {
                    if (Eat('+')) result = checked(result + Product());
                    else if (Eat('-')) result = checked(result - Product());
                    else return result;
                }
            }
            finally { _depth--; }
        }

        private decimal Product()
        {
            Enter();
            try
            {
                decimal result = Unary();
                while (true)
                {
                    if (Eat('*')) result = Multiply(result, Unary());
                    else if (Eat('/')) result = Divide(result, Unary());
                    else return result;
                }
            }
            finally { _depth--; }
        }

        private decimal Unary()
        {
            Enter();
            try
            {
                if (Eat('+')) return Unary();
                if (Eat('-')) return checked(-Unary());
                return Power();
            }
            finally { _depth--; }
        }

        private decimal Power()
        {
            Enter();
            try
            {
                decimal b = Postfix();
                // Right-associative: 2^3^2 = 2^(3^2); unary binds looser than ^.
                if (Eat('^')) b = IntegerPower(b, Unary());
                return b;
            }
            finally { _depth--; }
        }

        private decimal Postfix()
        {
            Enter();
            try
            {
                decimal number = Primary();
                while (Eat('%')) number = Divide(number, 100m);
                return number;
            }
            finally { _depth--; }
        }

        private decimal Primary()
        {
            Enter();
            try
            {
                if (Eat('('))
                {
                    decimal groupedValue = Sum();
                    if (!Eat(')')) throw Error("Expected closing parenthesis");
                    return groupedValue;
                }
                SkipWhitespace();
                if (_index < _source.Length &&
                    (char.IsAsciiDigit(_source[_index]) || _source[_index] == '.'))
                    return ParseNumber();

                string name = ParseName();
                if (name.Length == 0) throw Error("Expected a number, variable or function");
                if (Eat('('))
                {
                    var args = new List<decimal>();
                    if (!Eat(')'))
                    {
                        while (true)
                        {
                            if (args.Count == 16) throw Error("Too many function arguments");
                            args.Add(Sum());
                            if (Eat(')')) break;
                            if (!Eat(',')) throw Error("Expected comma between function arguments");
                        }
                    }
                    return Call(name, args);
                }
                if (_variables.TryGetValue(name, out decimal value)) return value;
                // Defensively support externally supplied case-insensitive dictionaries too.
                foreach (KeyValuePair<string, decimal> variable in _variables)
                    if (string.Equals(variable.Key, name, StringComparison.OrdinalIgnoreCase))
                        return variable.Value;
                throw Error("Unknown variable: " + name);
            }
            finally { _depth--; }
        }

        private decimal ParseNumber()
        {
            int start = _index;
            bool digits = false;
            while (_index < _source.Length && char.IsAsciiDigit(_source[_index])) { _index++; digits = true; }
            if (_index < _source.Length && _source[_index] == '.')
            {
                _index++;
                while (_index < _source.Length && char.IsAsciiDigit(_source[_index])) { _index++; digits = true; }
            }
            if (!digits) throw Error("Decimal point must have a digit");
            if (_index < _source.Length && _source[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < _source.Length && _source[_index] is '+' or '-') _index++;
                int exponentStart = _index;
                while (_index < _source.Length && char.IsAsciiDigit(_source[_index])) _index++;
                if (_index == exponentStart) throw Error("Invalid scientific-notation exponent");
            }
            return ParseFiniteDecimal(_source[start.._index]);
        }

        private string ParseName()
        {
            SkipWhitespace();
            int start = _index;
            if (_index < _source.Length &&
                (_source[_index] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_'))
            {
                _index++;
                while (_index < _source.Length &&
                    (_source[_index] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                    _index++;
            }
            return _source[start.._index];
        }

        private decimal Call(string name, IReadOnlyList<decimal> args)
        {
            string id = name.ToLowerInvariant();
            switch (id)
            {
                case "abs" when args.Count == 1: return Math.Abs(args[0]);
                case "sqrt" when args.Count == 1: return Sqrt(args[0]);
                case "min" when args.Count >= 2: return args.Min();
                case "max" when args.Count >= 2: return args.Max();
                case "round" when args.Count is 1 or 2:
                    int places = args.Count == 1 ? 0 : WholeInteger(args[1], 0, 12, "round places");
                    return decimal.Round(args[0], places, MidpointRounding.AwayFromZero);
                default:
                    throw Error("Unsupported function or argument count: " + name);
            }
        }

        private static decimal Sqrt(decimal number)
        {
            if (number < 0m) throw new InvalidOperationException("Square root requires a nonnegative operand.");
            if (number == 0m) return 0m;
            decimal guess = (decimal)Math.Sqrt((double)number);
            for (int i = 0; i < 48; i++)
            {
                decimal next = (guess + number / guess) / 2m;
                if (next == guess) break;
                guess = next;
            }
            return guess;
        }

        private static decimal IntegerPower(decimal basis, decimal exponent)
        {
            int n = WholeInteger(exponent, -64, 64, "Exponent");
            if (basis == 0m && n < 0)
                throw new InvalidOperationException("Zero cannot be raised to a negative power.");
            decimal result = 1m;
            decimal factor = basis;
            int remaining = Math.Abs(n);
            while (remaining > 0)
            {
                if ((remaining & 1) != 0) result = Multiply(result, factor);
                remaining /= 2;
                if (remaining > 0) factor = Multiply(factor, factor);
            }
            return n < 0 ? Divide(1m, result) : result;
        }

        private static decimal Multiply(decimal left, decimal right)
        {
            decimal result = checked(left * right);
            if (left != 0m && right != 0m && result == 0m)
                throw new InvalidOperationException("Nonzero decimal multiplication underflowed the supported precision.");
            return result;
        }

        private static decimal Divide(decimal numerator, decimal denominator)
        {
            decimal result = numerator / denominator;
            if (numerator != 0m && result == 0m)
                throw new InvalidOperationException("Nonzero decimal division underflowed the supported precision.");
            return result;
        }

        private static int WholeInteger(decimal value, int minimum, int maximum, string label)
        {
            if (value != decimal.Truncate(value) || value < minimum || value > maximum)
                throw new InvalidOperationException($"{label} must be a whole number from {minimum} to {maximum}.");
            return (int)value;
        }

        private bool Eat(char symbol)
        {
            SkipWhitespace();
            if (_index >= _source.Length || _source[_index] != symbol) return false;
            _index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < _source.Length && char.IsWhiteSpace(_source[_index])) _index++;
        }

        private void Enter()
        {
            _token.ThrowIfCancellationRequested();
            if (++_operations > 512 || ++_depth > 64)
                throw new InvalidOperationException("Expression complexity exceeds the safe evaluation limit.");
        }

        private InvalidOperationException Error(string message) =>
            new($"{message} (near character {_index + 1}).");
    }
}
