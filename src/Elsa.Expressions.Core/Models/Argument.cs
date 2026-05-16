using Elsa.Expressions.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.Core.Models
{
    public abstract class Argument
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Argument"/> class.
        /// </summary>
        protected Argument()
        {
        }

        /// <inheritdoc />
        protected Argument(IMemoryBlockReference memoryBlockReference) : this(() => memoryBlockReference)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Argument"/> class.
        /// </summary>
        /// <param name="memoryBlockReference"></param>
        protected Argument(Func<IMemoryBlockReference> memoryBlockReference)
        {
            MemoryBlockReference = memoryBlockReference;
        }

        /// <summary>
        /// Gets or sets the memory block reference.
        /// </summary>
        [JsonIgnore]
        public Func<IMemoryBlockReference> MemoryBlockReference { get; set; } = null!;
    }
}
