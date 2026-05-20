using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Expressions.JavaScript.Activities.RunJavaScript
{
    internal static class ActivityDefinition
    {
        internal static readonly Elsa.Activities.Design.Core.Models.ActivityDefinition Instance = CreateInstance();

        private static Elsa.Activities.Design.Core.Models.ActivityDefinition CreateInstance()
        {
            var assemblyName = typeof(Activity).Assembly.GetName();
            var version = assemblyName.Version?.ToString() ?? "";

            return new()
            {
                Id = "RunJavaScriptUsingJint",
                UniqueName = "Run JavaScript",
                Namespace = typeof(Activity).Namespace!,
                AssemblyName = assemblyName.FullName,
                AssemblyVersion = version,
                Category = "Javascript",
                Description = "Runs a script using JInt engine",
                DisplayName = "Run JavaScript (Jint)",
                FullyQualifiedTypeName = typeof(Activity).FullName!,
                Inputs =
                [
                    new ActivityPropertyDefinition(
                        "RunJavaScriptUsingJint_ScriptInput",
                        "Script",
                        TypeInformation.String,
                        "Script",
                        "The JavaScript code to execute."
                    )
                ],
                Outputs =
                [
                    new ActivityPropertyDefinition(
                        "RunJavaScriptUsingJint_ResultOutput",
                        "Result",
                        TypeInformation.Object,
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
