using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Elsa.Serialization.Core.Options
{
    /// <summary>
    /// Provides options to the conversion method.
    /// </summary>
    public record ObjectConverterOptions(
        JsonSerializerOptions? SerializerOptions = null,
        IWellKnownTypeRegistry? WellKnownTypeRegistry = null,
        bool DeserializeJsonObjectToObject = false,
        bool? StrictMode = null
    );
}
