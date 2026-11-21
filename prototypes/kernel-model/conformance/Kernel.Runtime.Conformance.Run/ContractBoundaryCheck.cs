using System.Reflection;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

internal static class ContractBoundaryCheck
{
    private static readonly string[] ImplementationWords =
        ["Partition", "Epoch", "Fence", "Lease", "Position", "NodeRecord", "KernelStore", "Sql", "SQL"];

    public static int Run()
    {
        var foundation = typeof(ActorRef).Assembly;
        var model = typeof(EffectKey).Assembly;
        var faults = new List<string>();
        Check(foundation, null, faults);
        Check(model, foundation.GetName().Name, faults);
        foreach (var fault in faults)
            Console.Error.WriteLine(fault);
        if (faults.Count == 0)
            Console.WriteLine("ok the contract packages expose no implementation names or references");
        return faults.Count == 0 ? 0 : 1;
    }

    private static void Check(Assembly assembly, string? allowedContract, List<string> faults)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
            if (reference.Name is { } name && name != allowedContract && name is not ("System" or "netstandard") && !name.StartsWith("System.", StringComparison.Ordinal))
                faults.Add($"{assembly.GetName().Name} references {name}");

        foreach (var type in assembly.GetExportedTypes())
        {
            CheckName(type.Name, type.FullName!, faults);
            const BindingFlags publicMembers = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMembers(publicMembers))
            {
                CheckName(member.Name, $"{type.FullName}.{member.Name}", faults);
                foreach (var signature in Signature(member))
                    CheckType(signature, $"{type.FullName}.{member.Name} signature", faults);
            }
        }
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo { EventHandlerType: { } handler } => [handler],
        _ => [],
    };

    private static void CheckType(Type type, string location, List<string> faults)
    {
        CheckName(type.Name, location, faults);
        if (type.GetElementType() is { } element) CheckType(element, location, faults);
        foreach (var argument in type.GenericTypeArguments) CheckType(argument, location, faults);
    }

    private static void CheckName(string name, string location, List<string> faults)
    {
        if (ImplementationWords.Any(word => name.Contains(word, StringComparison.Ordinal)))
            faults.Add($"{location} exposes implementation vocabulary ({name})");
    }
}
