using System.Globalization;
using System.Numerics;

namespace Elsa.Workflows.Dashboard.Queries;

/// <summary>Exact comparisons happen before rounding: a displayed equality must not hide a real rate increase.</summary>
internal sealed record WorkflowQueryNumber(BigInteger Numerator, BigInteger Denominator) : IComparable<WorkflowQueryNumber>
{
    public static WorkflowQueryNumber From(decimal value)
    {
        var bits = decimal.GetBits(value);
        var numerator = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) numerator = -numerator;
        return new(numerator, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }

    public WorkflowQueryNumber Subtract(WorkflowQueryNumber other) =>
        new(Numerator * other.Denominator - other.Numerator * Denominator, Denominator * other.Denominator);

    public int CompareTo(WorkflowQueryNumber? other) => other is null ? 1 :
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public string Format()
    {
        // Only transport/display is rounded; predicates and ordering use the exact fraction above.
        const long precision = 1_000_000_000_000;
        var scaled = BigInteger.DivRem(Numerator * precision, Denominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= Denominator) scaled += Numerator.Sign;
        var whole = BigInteger.DivRem(scaled, precision, out var fraction);
        return ((decimal)whole + (decimal)fraction / precision).ToString("0.############", CultureInfo.InvariantCulture);
    }
}
