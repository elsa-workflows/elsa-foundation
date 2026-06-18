namespace Elsa.Persistence.Core.Queries;

/// <summary>
/// The complete, closed set of comparison operators a <see cref="Query{TEntity}"/> may use.
/// This is deliberately tiny: it is the lowest-common-denominator every persistence provider
/// (EF Core, Groundwork, ...) must be able to translate. Do not grow it without first proving
/// every target provider can honour the new operator natively.
/// </summary>
public enum QueryOp
{
    /// <summary>Field equals the supplied value.</summary>
    Equal,

    /// <summary>Field is contained in the supplied set of values (SQL <c>IN</c>).</summary>
    In,

    /// <summary>Field contains the supplied substring, case-insensitively (SQL <c>LIKE '%v%'</c>).</summary>
    Contains
}
