namespace Elsa.Expressions.JavaScript.Jint.Options
{
    public sealed class FeatureOptions
    {      
        public bool AllowClrAccess { get; set; }
      
        public TimeSpan? ScriptCacheTimeout { get; set; } = TimeSpan.FromDays(1);
    }
}
