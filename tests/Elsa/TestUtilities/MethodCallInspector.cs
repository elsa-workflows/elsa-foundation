using System.Reflection;
using System.Reflection.Emit;

namespace Elsa.Testing.Reflection;

public static class MethodCallInspector
{
    private static readonly HashSet<string> ServiceLocatorMethodNames =
    [
        "GetService",
        "GetRequiredService",
        "GetServices",
        "GetKeyedService",
        "GetRequiredKeyedService",
        "GetKeyedServices"
    ];

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(opCode => opCode.Value)
        .ToDictionary(group => group.Key, group => group.First());

    public static IReadOnlyList<MethodBase> GetCalledMethods(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var body = method.GetMethodBody() ?? throw new InvalidOperationException($"Method '{method}' has no body.");
        var bytes = body.GetILAsByteArray() ?? throw new InvalidOperationException($"Method '{method}' has no IL body.");
        var calledMethods = new List<MethodBase>();

        for (var offset = 0; offset < bytes.Length;)
        {
            var opCode = ReadOpCode(bytes, ref offset);
            var operandOffset = offset;

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var metadataToken = BitConverter.ToInt32(bytes, operandOffset);
                var calledMethod = method.Module.ResolveMethod(
                    metadataToken,
                    method.DeclaringType?.GetGenericArguments(),
                    method.GetGenericArguments());
                if (calledMethod is not null)
                    calledMethods.Add(calledMethod);
            }

            offset += OperandSize(opCode.OperandType, bytes, operandOffset);
        }

        return calledMethods;
    }

    public static bool IsServiceLocatorOrProviderBuildCall(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var declaringType = method.DeclaringType;
        var declaringTypeName = declaringType?.FullName;
        var methodName = method.Name[(method.Name.LastIndexOf('.') + 1)..];

        if (declaringType is not null &&
            typeof(IServiceProvider).IsAssignableFrom(declaringType) &&
            ServiceLocatorMethodNames.Contains(methodName))
            return true;

        if (declaringTypeName is
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions" or
                "Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions" &&
            ServiceLocatorMethodNames.Contains(methodName))
            return true;

        return method is MethodInfo methodInfo &&
               typeof(IServiceProvider).IsAssignableFrom(methodInfo.ReturnType) &&
               methodName is "BuildServiceProvider" or "CreateServiceProvider";
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int offset)
    {
        short value = bytes[offset++];
        if (value == 0xfe)
            value = unchecked((short)(0xfe00 | bytes[offset++]));

        return OpCodesByValue.TryGetValue(value, out var opCode)
            ? opCode
            : throw new InvalidOperationException($"Unknown IL opcode 0x{value:x4}.");
    }

    private static int OperandSize(OperandType operandType, byte[] bytes, int operandOffset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, operandOffset),
        _ => throw new InvalidOperationException($"Unsupported IL operand type '{operandType}'.")
    };
}
