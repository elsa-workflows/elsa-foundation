using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Options
{
    public sealed class TypedActivityProviderOptions
    {
        public HashSet<Type> ActivityTypes { get; set; } = [];
    }
}
