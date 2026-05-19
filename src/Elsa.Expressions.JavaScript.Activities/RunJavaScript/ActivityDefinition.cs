using Elsa.Activities.Design.Core.Models;

namespace Elsa.Expressions.JavaScript.Activities.RunJavaScript
{
    internal static class ActivityDefinition
    {
        internal static readonly Elsa.Activities.Design.Core.Models.ActivityDefinition Instance = CreateInstance();

        static Elsa.Activities.Design.Core.Models.ActivityDefinition CreateInstance()
        {
            var assemblyName = typeof(Activity).Assembly.GetName();
            var version = assemblyName.Version?.ToString() ?? "";

            return new()
            {
                Id = "RunJavaScriptUsingJint",
                Name = "Run JavaScript",
                Namespace = typeof(Activity).Namespace!,
                AssemblyName = assemblyName.FullName,
                AssemblyVersion = version,
                Category = "Javascript",
                Description = "Runs a script using JInt engine",
                DisplayName = "Run JavaScript (Jint)",
                FullyQualifiedName = typeof(Activity).FullName!,
                Inputs = 
                [
                    new ActivityPropertyDefinition<string>(
                        "RunJavaScriptUsingJint_ScriptInput",
                        "Script",
                        "Script",
                        "The JavaScript code to execute."
                    )
                ],
                Outputs =
                [
                    new ActivityPropertyDefinition<object>(
                        "RunJavaScriptUsingJint_ResultOutput",
                        "Result",
                        "Result",
                        "The result of the JavaScript execution."
                    )
                ],
                IsBrowsable = true,
                Kind = ActivityKind.Action                
            };
        }
    }
}
