using Acornima.Ast;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Jint;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    internal sealed class PreparedScriptFactory(IMemoryCache memoryCache, IOptions<JintFeatureOptions> options) : IPreparedScriptFactory
    {
        public Prepared<Script> Create(string expression)
        {
            var cacheKey = "jint:script:" + Hash(expression);

            return memoryCache.GetOrCreate(cacheKey, entry =>
            {
                if (options.Value.ScriptCacheTimeout.HasValue)
                    entry.SetSlidingExpiration(options.Value.ScriptCacheTimeout.Value);

                return PrepareScript(expression);
            })!;
        }

        static private Prepared<Script> PrepareScript(string expression)
        {
            var prepareOptions = new ScriptPreparationOptions
            {
                ParsingOptions = new()
                {
                    AllowReturnOutsideFunction = true
                }
            };
            return Engine.PrepareScript(expression, options: prepareOptions);
        }

        private static string Hash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
