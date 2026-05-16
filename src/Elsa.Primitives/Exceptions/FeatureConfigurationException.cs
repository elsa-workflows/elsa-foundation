using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Primitives.Exceptions
{
    public sealed class FeatureConfigurationException : Exception
    {
        public FeatureConfigurationException()
        {
        }

        public FeatureConfigurationException(string? message) : base(message)
        {
        }
    }
}
