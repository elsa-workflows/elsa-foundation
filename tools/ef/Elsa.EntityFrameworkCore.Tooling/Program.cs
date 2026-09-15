// `list` prints one line per design-time factory: <Context>|<Provider>|<ModuleAssembly>|<ContextNamespace>.
// tools/ef/generate-module-migrations.sh consumes it; dotnet ef only needs the factory types.
if (args is not ["list"])
{
    Console.Error.WriteLine("Usage: Elsa.EntityFrameworkCore.Tooling list");
    return 2;
}

var factories = typeof(Program).Assembly.GetTypes()
    .Where(type => type is { IsAbstract: false, BaseType.IsGenericType: true } &&
                   type.BaseType.GetGenericTypeDefinition() == typeof(Elsa.EntityFrameworkCore.Tooling.ModuleDesignTimeFactory<>))
    .OrderBy(type => type.Name, StringComparer.Ordinal);
foreach (var factory in factories)
{
    var context = factory.BaseType!.GetGenericArguments()[0];
    var provider = (string)factory.BaseType.GetProperty("Provider")!.GetValue(null)!;
    Console.WriteLine($"{context.Name}|{provider}|{context.Assembly.GetName().Name}|{context.Namespace}");
}

return 0;
