namespace Elsa.Expressions.Core.Contracts
{
    public interface IMemoryRegister
    {
        /// <summary>
        /// The memory blocks declared in this register.
        /// </summary>
        IDictionary<string, IMemoryBlock> Blocks { get; }

        /// <summary>
        /// Returns true if the specified memory block is declared in this register.
        /// </summary>
        bool IsDeclared(IMemoryBlockReference reference) => HasBlock(reference.Id);

        /// <summary>
        /// Returns true if the specified memory block is declared in this register.
        /// </summary>
        bool HasBlock(string id) => Blocks.ContainsKey(id);

        /// <summary>
        /// Returns the memory block with the specified ID.
        /// </summary>
        bool TryGetBlock(string id, out IMemoryBlock block)
        {
            return Blocks.TryGetValue(id, out block!);
        }

        /// <summary>
        /// Declares the memory for the specified memory block references. 
        /// </summary>
        void Declare(IEnumerable<IMemoryBlockReference> references)
        {
            foreach (var reference in references)
                Declare(reference);
        }

        /// <summary>
        /// Declares the memory for the specified memory block reference.
        /// </summary>
        IMemoryBlock Declare(IMemoryBlockReference blockReference)
        {
            if (Blocks.TryGetValue(blockReference.Id, out var block))
                return block;

            block = blockReference.Declare();
            Blocks[blockReference.Id] = block;
            return block;
        }
    }
}
