namespace Elsa.Expressions.Core.Contracts
{
    public interface IMemoryBlockReferenceFactory
    {
        IMemoryBlockReference Create(string id);

        IMemoryBlockReference Create(string id, object? value);

        IMemoryBlockReference Create<TValue>(string id, TValue? value) => Create(id, value);
    }
}
