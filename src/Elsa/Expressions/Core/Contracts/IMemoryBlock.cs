namespace Elsa.Expressions.Core.Contracts;

public interface IMemoryBlock
{
    /// <summary>
    /// The value stored in this block.
    /// </summary>
    object? Value { get; set; }

    /// <summary>
    /// Optional metadata about this block.
    /// </summary>
    object? Metadata { get; set; }
}