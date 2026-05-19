using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint3.Settings
{
    public sealed class JintEngineFactorySettings
    {
        /// <summary>
        /// Enables access to any .NET class. Do not enable if you are executing workflows from untrusted sources (e.g. user defined workflows).
        ///
        /// See Jint docs for more: https://github.com/sebastienros/jint#accessing-net-assemblies-and-classes
        /// </summary>
        public bool AllowClrAccess { get; set; }

        /// <summary>
        /// Enables access to .NET configuration via the <c>getConfig</c> function.
        /// Do not enable if you are executing workflows from untrusted sources (e.g user defined workflows).
        /// </summary>
        public bool AllowConfigurationAccess { get; set; }

        /// <summary>
        /// The timeout for script caching.
        /// </summary>
        /// <remarks>
        /// The <c>ScriptCacheTimeout</c> property specifies the duration for which the scripts are cached in the Jint JavaScript engine. When a script is executed, it is compiled and cached for future use. This caching improves performance by avoiding repetitive compilation of the same script.
        /// If the value of <c>ScriptCacheTimeout</c> is <c>null</c>, the scripts are cached indefinitely. If a time value is specified, the scripts will be purged from the cache after they've been unused for the specified duration and recompiled on next use.
        /// </remarks>
        public TimeSpan? ScriptCacheTimeout { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Disables the generation of variable wrappers. E.g. <c>getMyVariable()</c> will no longer be available for variables. Instead, you can only access variables using <c>getVariable("MyVariable")</c> function.
        /// This is useful if your application requires the use of invalid JavaScript variable names.
        /// </summary>
        public bool DisableWrappers { get; set; }
    }
}
