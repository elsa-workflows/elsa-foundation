using Acornima;
using Acornima.Ast;

namespace Elsa3.Mapping.Services;

/// <summary>The JSON kind an Elsa 3 script's result has, as far as its source shows it (spec 1228 US6, FR-023).</summary>
internal enum Elsa3ScriptResultKind
{
    /// <summary>The source does not show it: a variable, a call, a mix of kinds.</summary>
    Unknown,
    Boolean,
    String,
    /// <summary>A number that may have a fraction.</summary>
    Number,
    /// <summary>
    /// A number the source shows is an integer: <c>Math.round(…)</c>, <c>parseInt(…)</c>, integer arithmetic, or an integer
    /// literal Elsa 3 held exactly (within ±(2^53 − 1)) but not to 15 significant digits.
    /// </summary>
    Integer,
    /// <summary>
    /// An integer literal of at most 15 significant digits: Elsa 3's conversions keep it exactly into any integral or
    /// decimal input, as Elsa 4's do.
    /// </summary>
    ExactIntegerLiteral,
    /// <summary>
    /// A fraction written as a literal with at most 15 significant digits (<c>12.5</c>): Elsa 3's double-to-decimal
    /// conversion keeps it exactly, so a decimal input gets the same value in both.
    /// </summary>
    ExactFractionLiteral,
    Null,
    Object,
    Array
}

/// <summary>
/// Infers the kind of an Elsa 3 <c>RunJavaScript</c> result from its source (spec 1228 US6, FR-023, FR-024): the script's
/// completion value, its last expression statement, together with every top-level <c>return</c>. Each must show the same
/// kind; anything the source does not show is <see cref="Elsa3ScriptResultKind.Unknown"/>. The import uses it to keep a
/// binding of the untyped result to a typed input only where Elsa 4 gives the value Elsa 3 did (research R-5).
/// </summary>
internal static class Elsa3ScriptResultKinds
{
    public static Elsa3ScriptResultKind Infer(string source)
    {
        Script script;
        try
        {
            script = new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source);
        }
        catch (ParseErrorException)
        {
            return Elsa3ScriptResultKind.Unknown;
        }

        var results = new List<Expression?>();
        CollectReturns(script.Body, results);
        if (script.Body.LastOrDefault() is ExpressionStatement { Expression: var completion })
            results.Add(completion);
        if (results.Count == 0)
            return Elsa3ScriptResultKind.Unknown;

        var kinds = results.Select(result => result is null ? Elsa3ScriptResultKind.Unknown : Of(result)).Distinct().ToArray();
        return kinds.Length == 1 ? kinds[0] : Join(kinds);
    }

    /// <summary>Whether a result of <paramref name="kind"/> converts into <paramref name="target"/> to the value Elsa 3 gave.</summary>
    public static bool Fits(Elsa3ScriptResultKind kind, Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Boolean => kind == Elsa3ScriptResultKind.Boolean,
        Elsa3ScalarTarget.String => kind == Elsa3ScriptResultKind.String,
        // Elsa 3 rounded a fraction into an integral target (Convert.ChangeType); Elsa 4 refuses it, so only an integer fits.
        Elsa3ScalarTarget.Integer => IsInteger(kind),
        Elsa3ScalarTarget.Number => IsNumber(kind),
        // Elsa 3 converted the double to decimal at 15 significant digits (Convert.ToDecimal(double)); Elsa 4 reads the
        // JSON text exactly (0.1 + 0.2 is 0.3 in Elsa 3 and 0.30000000000000004 in Elsa 4). Only a literal Elsa 3 kept
        // exactly is the same: a computed number, integer or not, may have more than 15 significant digits.
        Elsa3ScalarTarget.Decimal => kind is Elsa3ScriptResultKind.ExactIntegerLiteral or Elsa3ScriptResultKind.ExactFractionLiteral,
        _ => false
    };

    /// <summary>The scalar target an Elsa 4 input type is, or null when it is not one this check covers.</summary>
    public static Elsa3ScalarTarget? ScalarTarget(string simpleName) => simpleName switch
    {
        "Boolean" or "bool" => Elsa3ScalarTarget.Boolean,
        "String" or "string" => Elsa3ScalarTarget.String,
        "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "Byte" or "SByte" or "int" or "long" => Elsa3ScalarTarget.Integer,
        "Single" or "Double" or "float" or "double" => Elsa3ScalarTarget.Number,
        "Decimal" or "decimal" => Elsa3ScalarTarget.Decimal,
        _ => null
    };

    public static string Describe(Elsa3ScriptResultKind kind) => kind switch
    {
        Elsa3ScriptResultKind.Boolean => "a boolean",
        Elsa3ScriptResultKind.String => "a string",
        Elsa3ScriptResultKind.Number => "a number that may have a fraction",
        Elsa3ScriptResultKind.Integer => "an integer",
        Elsa3ScriptResultKind.ExactIntegerLiteral => "an integer",
        Elsa3ScriptResultKind.ExactFractionLiteral => "a number literal",
        Elsa3ScriptResultKind.Null => "null",
        Elsa3ScriptResultKind.Object => "an object",
        Elsa3ScriptResultKind.Array => "an array",
        _ => "a value whose kind the script does not show"
    };

    /// <summary>Why the result may not reach the input as Elsa 3 gave it, for the refusal's message.</summary>
    public static string Difference(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Decimal =>
            "Elsa 3 converted a computed number into it at 15 significant digits; Elsa 4 reads the number exactly, so a fraction " +
            "can take a different value",
        Elsa3ScalarTarget.Integer =>
            "Elsa 3 converted any value into it and rounded a fraction; Elsa 4 converts only an integer, so the result could take " +
            "a different value or fail at run time",
        _ => $"Elsa 3 converted any value into it; Elsa 4 converts only {Expected(target)}, so the result could take a different " +
             "value or fail at run time"
    };

    /// <summary>
    /// Why a value whose kind nothing shows (a loop item, an event payload, HTTP data) may not reach the input as Elsa 3
    /// gave it (#4898): it may be of any kind, not only a number.
    /// </summary>
    public static string ReadDifference(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Decimal =>
            "Elsa 3 converted any value into it, a computed number at 15 significant digits; Elsa 4 converts only a number and " +
            "reads it exactly, so the value could differ or fail at run time",
        _ => Difference(target)
    };

    /// <summary>What to change in the script, for the refusal's guidance.</summary>
    public static string Guidance(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Decimal =>
            "Return an integer (for example an amount in cents) or a number literal, or bind the input to a value that is already " +
            "a decimal, before import. Rounding the value would change it.",
        Elsa3ScalarTarget.Integer => "Make the script return an integer explicitly, for example by wrapping its value in `Math.round(…)`, before import.",
        Elsa3ScalarTarget.Boolean => "Make the script return a boolean explicitly, for example by wrapping its value in `Boolean(…)`, before import.",
        Elsa3ScalarTarget.String => "Make the script return a string explicitly, for example by wrapping its value in `String(…)`, before import.",
        _ => "Make the script return a number explicitly, for example by wrapping its value in `Number(…)`, before import."
    };

    /// <summary>
    /// What to change when the producer is not a script (#4898): a loop item, an event payload, parsed content. Nothing in
    /// the definition shows their kind, so the input states it in a JavaScript expression in the Elsa 3 workflow.
    /// </summary>
    public static string ReadGuidance(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Decimal =>
            "Bind the input to a JavaScript expression that returns an integer (for example an amount in cents), or to a value " +
            "that is already a decimal, before import. Rounding the value would change it.",
        Elsa3ScalarTarget.Integer => "Bind the input to a JavaScript expression that converts the value explicitly, for example `Math.round(…)`, before import.",
        Elsa3ScalarTarget.Boolean => "Bind the input to a JavaScript expression that converts the value explicitly, for example `Boolean(…)`, before import.",
        Elsa3ScalarTarget.String => "Bind the input to a JavaScript expression that converts the value explicitly, for example `String(…)`, before import.",
        _ => "Bind the input to a JavaScript expression that converts the value explicitly, for example `Number(…)`, before import."
    };

    private static string Expected(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Boolean => "a boolean",
        Elsa3ScalarTarget.String => "a string",
        _ => "a number"
    };

    // The top-level returns: not those inside a function, which return from that function.
    private static void CollectReturns(IEnumerable<Node> nodes, List<Expression?> results)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ReturnStatement returned:
                    results.Add(returned.Argument);
                    break;
                case IFunction:
                    break;
                default:
                    CollectReturns(node.ChildNodes, results);
                    break;
            }
        }
    }

    private static Elsa3ScriptResultKind Of(Expression expression) => expression switch
    {
        Literal { Kind: TokenKind.BooleanLiteral } => Elsa3ScriptResultKind.Boolean,
        Literal { Kind: TokenKind.StringLiteral } => Elsa3ScriptResultKind.String,
        Literal { Kind: TokenKind.NullLiteral } => Elsa3ScriptResultKind.Null,
        Literal { Kind: TokenKind.NumericLiteral, Value: double number } => NumberLiteral(number),
        TemplateLiteral => Elsa3ScriptResultKind.String,
        ObjectExpression => Elsa3ScriptResultKind.Object,
        ArrayExpression => Elsa3ScriptResultKind.Array,
        ParenthesizedExpression parenthesized => Of(parenthesized.Expression),
        SequenceExpression sequence => Of(sequence.Expressions[^1]),
        NonUpdateUnaryExpression unary => unary.Operator switch
        {
            Operator.LogicalNot => Elsa3ScriptResultKind.Boolean,
            Operator.TypeOf => Elsa3ScriptResultKind.String,
            Operator.UnaryNegation or Operator.UnaryPlus => Of(unary.Argument) is var operand &&
                                                             operand is Elsa3ScriptResultKind.Integer or Elsa3ScriptResultKind.ExactIntegerLiteral or Elsa3ScriptResultKind.ExactFractionLiteral
                ? operand
                : Elsa3ScriptResultKind.Number,
            _ => Elsa3ScriptResultKind.Unknown
        },
        LogicalExpression logical => logical.Operator is Operator.LogicalAnd or Operator.LogicalOr or Operator.NullishCoalescing
            ? Join([Of(logical.Left), Of(logical.Right)])
            : Elsa3ScriptResultKind.Unknown,
        BinaryExpression binary => Binary(binary),
        ConditionalExpression conditional => Join([Of(conditional.Consequent), Of(conditional.Alternate)]),
        CallExpression { Callee: Identifier { Name: "Boolean" } } => Elsa3ScriptResultKind.Boolean,
        CallExpression { Callee: Identifier { Name: "String" } } => Elsa3ScriptResultKind.String,
        CallExpression { Callee: Identifier { Name: "Number" or "parseFloat" } } => Elsa3ScriptResultKind.Number,
        CallExpression { Callee: Identifier { Name: "parseInt" } } => Elsa3ScriptResultKind.Integer,
        CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Math" }, Property: Identifier { Name: "round" or "floor" or "ceil" or "trunc" } } } => Elsa3ScriptResultKind.Integer,
        _ => Elsa3ScriptResultKind.Unknown
    };

    private static Elsa3ScriptResultKind Binary(BinaryExpression binary)
    {
        // Arithmetic on literals is folded, so its value is classified like a literal (1e8 * 1e8 is past 2^53 - 1).
        if (TryFold(binary, out var folded))
            return NumberLiteral(folded);
        switch (binary.Operator)
        {
            case Operator.Equality or Operator.Inequality or Operator.StrictEquality or Operator.StrictInequality
                or Operator.LessThan or Operator.LessThanOrEqual or Operator.GreaterThan or Operator.GreaterThanOrEqual
                or Operator.InstanceOf or Operator.In:
                return Elsa3ScriptResultKind.Boolean;
            case Operator.Addition:
                var left = binary.Left is Expression l ? Of(l) : Elsa3ScriptResultKind.Unknown;
                var right = Of(binary.Right);
                if (left == Elsa3ScriptResultKind.String || right == Elsa3ScriptResultKind.String)
                    return Elsa3ScriptResultKind.String;
                // A sum is computed: an integer only from two integers, otherwise a number (never an exact literal).
                if (!IsNumber(left) || !IsNumber(right))
                    return Elsa3ScriptResultKind.Unknown;
                return IsInteger(left) && IsInteger(right) ? Elsa3ScriptResultKind.Integer : Elsa3ScriptResultKind.Number;
            case Operator.Subtraction or Operator.Multiplication or Operator.Remainder or Operator.Exponentiation:
                var a = binary.Left is Expression x ? Of(x) : Elsa3ScriptResultKind.Unknown;
                var b = Of(binary.Right);
                return IsInteger(a) && IsInteger(b) && binary.Operator != Operator.Exponentiation
                    ? Elsa3ScriptResultKind.Integer
                    : Elsa3ScriptResultKind.Number;
            case Operator.Division:
                return Elsa3ScriptResultKind.Number;
            case Operator.BitwiseAnd or Operator.BitwiseOr or Operator.BitwiseXor or Operator.LeftShift or Operator.RightShift or Operator.UnsignedRightShift:
                return Elsa3ScriptResultKind.Integer;
            default:
                return Elsa3ScriptResultKind.Unknown;
        }
    }

    private static bool IsNumber(Elsa3ScriptResultKind kind) =>
        kind is Elsa3ScriptResultKind.Number or Elsa3ScriptResultKind.ExactFractionLiteral || IsInteger(kind);

    private static bool IsInteger(Elsa3ScriptResultKind kind) =>
        kind is Elsa3ScriptResultKind.Integer or Elsa3ScriptResultKind.ExactIntegerLiteral;

    // The value of arithmetic on number literals (+, -, *, /, unary minus), when the source fixes it.
    private static bool TryFold(Node node, out double value)
    {
        switch (node)
        {
            case Literal { Kind: TokenKind.NumericLiteral, Value: double number }:
                value = number;
                return true;
            case ParenthesizedExpression parenthesized:
                return TryFold(parenthesized.Expression, out value);
            case NonUpdateUnaryExpression { Operator: Operator.UnaryNegation } negation when TryFold(negation.Argument, out var operand):
                value = -operand;
                return true;
            case BinaryExpression { Operator: Operator.Addition or Operator.Subtraction or Operator.Multiplication or Operator.Division } binary
                when TryFold(binary.Left, out var left) && TryFold(binary.Right, out var right):
                value = binary.Operator switch
                {
                    Operator.Addition => left + right,
                    Operator.Subtraction => left - right,
                    Operator.Multiplication => left * right,
                    _ => left / right
                };
                return double.IsFinite(value);
            default:
                value = 0;
                return false;
        }
    }

    // A number: exact when Elsa 3's double-to-decimal conversion (15 significant digits) and Elsa 4's reading of the JSON
    // text agree. An integer beyond ±(2^53 − 1) is refused by Elsa 4's precision policy when written, where Elsa 3 ran
    // it, so it is a plain number. A computed integer from values the source does not fix is not range-checked.
    private static Elsa3ScriptResultKind NumberLiteral(double number)
    {
        var integer = double.IsInteger(number);
        if (integer && Math.Abs(number) > 9007199254740991d)
            return Elsa3ScriptResultKind.Number;
        bool exact;
        try
        {
            var elsa3 = Convert.ToDecimal(number);
            var elsa4 = decimal.Parse(number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            exact = elsa3 == elsa4;
        }
        catch (OverflowException)
        {
            exact = false;
        }

        return (integer, exact) switch
        {
            (true, true) => Elsa3ScriptResultKind.ExactIntegerLiteral,
            (true, false) => Elsa3ScriptResultKind.Integer,
            (false, true) => Elsa3ScriptResultKind.ExactFractionLiteral,
            _ => Elsa3ScriptResultKind.Number
        };
    }

    // Two kinds that agree are that kind; an integer and a number are a number; anything else is unknown.
    private static Elsa3ScriptResultKind Join(IReadOnlyCollection<Elsa3ScriptResultKind> kinds)
    {
        var distinct = kinds.Distinct().ToArray();
        if (distinct.Length == 1)
            return distinct[0];
        if (distinct.All(IsInteger))
            return Elsa3ScriptResultKind.Integer;
        return distinct.All(IsNumber) ? Elsa3ScriptResultKind.Number : Elsa3ScriptResultKind.Unknown;
    }
}

/// <summary>A scalar input type, by what Elsa 4 converts into it (spec 1228 FR-025).</summary>
internal enum Elsa3ScalarTarget
{
    Boolean,
    String,
    Integer,
    Number,
    /// <summary>
    /// A decimal input: only an integer literal, or a fraction literal, of at most 15 significant digits converts to the
    /// value Elsa 3 gave.
    /// </summary>
    Decimal
}
