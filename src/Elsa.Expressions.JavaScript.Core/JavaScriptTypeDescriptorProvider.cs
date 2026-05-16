using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core
{
    public sealed class JavaScriptTypeDescriptorProvider(IEnumerable<JavaScriptTypeDescriptor> descriptors) : IJavaScriptTypeDescriptorProvider
    {
        public IEnumerable<JavaScriptTypeDescriptor> GetDescriptors() => descriptors;
    }
}
