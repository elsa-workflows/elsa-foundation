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
    /// <summary>A number the source shows is an integer: an integer literal, <c>Math.round(…)</c>, <c>parseInt(…)</c>.</summary>
    Integer,
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
        Elsa3ScalarTarget.Integer => kind == Elsa3ScriptResultKind.Integer,
        Elsa3ScalarTarget.Number => kind is Elsa3ScriptResultKind.Number or Elsa3ScriptResultKind.Integer,
        _ => false
    };

    /// <summary>The scalar target an Elsa 4 input type is, or null when it is not one this check covers.</summary>
    public static Elsa3ScalarTarget? ScalarTarget(string simpleName) => simpleName switch
    {
        "Boolean" or "bool" => Elsa3ScalarTarget.Boolean,
        "String" or "string" => Elsa3ScalarTarget.String,
        "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "Byte" or "SByte" or "int" or "long" => Elsa3ScalarTarget.Integer,
        "Single" or "Double" or "Decimal" or "float" or "double" or "decimal" => Elsa3ScalarTarget.Number,
        _ => null
    };

    public static string Describe(Elsa3ScriptResultKind kind) => kind switch
    {
        Elsa3ScriptResultKind.Boolean => "a boolean",
        Elsa3ScriptResultKind.String => "a string",
        Elsa3ScriptResultKind.Number => "a number that may have a fraction",
        Elsa3ScriptResultKind.Integer => "an integer",
        Elsa3ScriptResultKind.Null => "null",
        Elsa3ScriptResultKind.Object => "an object",
        Elsa3ScriptResultKind.Array => "an array",
        _ => "a value whose kind the script does not show"
    };

    public static string Describe(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Boolean => "a boolean",
        Elsa3ScalarTarget.String => "a string",
        Elsa3ScalarTarget.Integer => "an integer",
        _ => "a number"
    };

    /// <summary>How a script states the kind explicitly, for the refusal's guidance.</summary>
    public static string Wrapper(Elsa3ScalarTarget target) => target switch
    {
        Elsa3ScalarTarget.Boolean => "Boolean(…)",
        Elsa3ScalarTarget.String => "String(…)",
        Elsa3ScalarTarget.Integer => "Math.round(…)",
        _ => "Number(…)"
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
        Literal { Kind: TokenKind.NumericLiteral, Value: double number } => double.IsInteger(number) ? Elsa3ScriptResultKind.Integer : Elsa3ScriptResultKind.Number,
        TemplateLiteral => Elsa3ScriptResultKind.String,
        ObjectExpression => Elsa3ScriptResultKind.Object,
        ArrayExpression => Elsa3ScriptResultKind.Array,
        ParenthesizedExpression parenthesized => Of(parenthesized.Expression),
        SequenceExpression sequence => Of(sequence.Expressions[^1]),
        NonUpdateUnaryExpression unary => unary.Operator switch
        {
            Operator.LogicalNot => Elsa3ScriptResultKind.Boolean,
            Operator.TypeOf => Elsa3ScriptResultKind.String,
            Operator.UnaryNegation or Operator.UnaryPlus => Of(unary.Argument) == Elsa3ScriptResultKind.Integer ? Elsa3ScriptResultKind.Integer : Elsa3ScriptResultKind.Number,
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
                return IsNumber(left) && IsNumber(right) ? Join([left, right]) : Elsa3ScriptResultKind.Unknown;
            case Operator.Subtraction or Operator.Multiplication or Operator.Remainder or Operator.Exponentiation:
                var a = binary.Left is Expression x ? Of(x) : Elsa3ScriptResultKind.Unknown;
                var b = Of(binary.Right);
                return a == Elsa3ScriptResultKind.Integer && b == Elsa3ScriptResultKind.Integer && binary.Operator != Operator.Exponentiation
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

    private static bool IsNumber(Elsa3ScriptResultKind kind) => kind is Elsa3ScriptResultKind.Number or Elsa3ScriptResultKind.Integer;

    // Two kinds that agree are that kind; an integer and a number are a number; anything else is unknown.
    private static Elsa3ScriptResultKind Join(IReadOnlyCollection<Elsa3ScriptResultKind> kinds)
    {
        var distinct = kinds.Distinct().ToArray();
        if (distinct.Length == 1)
            return distinct[0];
        return distinct.All(IsNumber) ? Elsa3ScriptResultKind.Number : Elsa3ScriptResultKind.Unknown;
    }
}

/// <summary>A scalar input type, by what Elsa 4 converts into it (spec 1228 FR-025).</summary>
internal enum Elsa3ScalarTarget
{
    Boolean,
    String,
    Integer,
    Number
}
