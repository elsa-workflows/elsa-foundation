using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests.Fixtures;

/// <summary>
/// The precision suite of spec 1228 (T001, SC-001, SC-002): JSON numbers a JavaScript number holds exactly, and ones it
/// cannot (ADR 0046 V9). The notation may change (<c>2.0</c> → <c>2</c>, <c>1e21</c> → <c>1e+21</c>); the value may not.
/// </summary>
public static class PrecisionCases
{
    /// <summary>JSON number text, whether a double holds it exactly, and whether its value is an integer.</summary>
    public static TheoryData<string, bool, bool> All => new()
    {
        { "0", true, true },
        { "-0", true, true },
        { "42", true, true },
        { "10.50", true, false },
        { "2.0", true, true },
        { "0.5", true, false },
        { "0.1", true, false },
        { "0.30000000000000004", true, false },
        { "9007199254740991", true, true },
        { "-9007199254740991", true, true },
        { "9007199254740992", true, true },
        { "9007199254740993", false, true },
        { "-9007199254740993", false, true },
        { "18446744073709551616", false, true },
        { "123456789012345678901234567890", false, true },
        { "1e21", true, true },
        { "1e30", true, true },
        { "1e400", false, true },
        { "-1e400", false, true },
        { "1e-400", false, false },
        { "0.10000000000000000000000000001", false, false },
        { "0.1000000000000000055511151231257827021181583404541015625", false, false },
        { "1.5e300", true, true },
        { "4.9406564584124654e-324", false, false }
    };

    public const string LargeId = "9007199254740993";
    public const string LargeIdHeld = "9007199254740992";
}
