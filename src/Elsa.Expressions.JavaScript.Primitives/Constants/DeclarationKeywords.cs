namespace Elsa.Expressions.JavaScript.Primitives.Constants
{
    public static class DeclarationKeywords
    {
        public const string Class = "class";

        public const string Type = "type";

        public const string Interface = "interface";

        public const string Enum = "enum";

        public static string GetDeclarationKeyword(Type type) =>
          type switch
          {
              { IsInterface: true } => Interface,
              { IsClass: true } => Class,
              { IsEnum: true } => Enum,
              { IsValueType: true, IsEnum: false } => Class,
              _ => Interface
          };
    }
}
