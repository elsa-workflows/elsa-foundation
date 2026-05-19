using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint.Options
{
    public class ConfigurationAccessFunctionProviderOptions
    {
        public bool AllowConfigurationAccess { get; set; } = false;

        public IEnumerable<string> DisallowedSections { get; set; } = [];
    }
}
