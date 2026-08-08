using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// How a property's compiled initializer was classified by <see cref="InitializerDefaultReader"/>.
/// </summary>
public enum InitializerKind
{
    /// <summary>No initializer assignment was found for the property's backing field.</summary>
    None,

    /// <summary>A constant initializer was found and its value is carried on <see cref="PropertyInitializer.Value"/>.</summary>
    Constant,

    /// <summary>An initializer exists but is not a statically representable constant (computed value, object construction, conditional assignment).</summary>
    Unrecognized
}

/// <summary>
/// A classified property initializer. <see cref="Value"/> is a boxed primitive, string, or null (for <c>ldnull</c>).
/// <see cref="ViaNewobj"/> marks constants that flowed through a constructor call before the store — only
/// meaningful when the target property is <see cref="Nullable{T}"/> (the tolerated wrapping); for any other
/// property type the consumer must treat the initializer as not statically representable (e.g. a
/// <see cref="decimal"/> constant's component int would otherwise be misread as the value).
/// </summary>
public sealed record PropertyInitializer(InitializerKind Kind, object? Value = null, bool ViaNewobj = false)
{
    public static readonly PropertyInitializer None = new(InitializerKind.None);
    public static readonly PropertyInitializer Unrecognized = new(InitializerKind.Unrecognized);
}

/// <summary>
/// Reads C# auto-property initializer constants (<c>public ResponseMode Mode { get; set; } = ResponseMode.Async;</c>)
/// straight from constructor IL via System.Reflection.Metadata — no type is ever instantiated, so the read works for
/// reflection-only (<see cref="System.Reflection.MetadataLoadContext"/>) scans and never executes author code (G1,
/// spec 149 / RFC #1191).
/// </summary>
/// <remarks>
/// Recognized pattern per initializer: <c>ldarg.0; &lt;constant load&gt; [conv.*] [newobj Nullable&lt;T&gt;(..)]; stfld &lt;backing field&gt;</c>
/// with constant loads <c>ldc.i4.*</c> / <c>ldc.i8</c> / <c>ldc.r4</c> / <c>ldc.r8</c> / <c>ldstr</c> / <c>ldnull</c>.
/// Anything else that stores to a field is classified <see cref="InitializerKind.Unrecognized"/> — an honest
/// "no statically representable default" rather than a guess. Instructions after the first branch are treated as
/// conditional: a field stored there is downgraded to Unrecognized even if a pre-branch constant was seen.
/// Note that Roslyn elides initializers that assign the type's default value; callers must therefore treat
/// <see cref="InitializerKind.None"/> on a non-nullable value-type property as "default(T)".
/// </remarks>
public sealed class InitializerDefaultReader : IDisposable
{
    private readonly Dictionary<string, AssemblyInitializerIndex?> indexesByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Classifies the initializer of <paramref name="propertyName"/> declared on <paramref name="declaringType"/>.
    /// Any read/parse failure classifies as <see cref="InitializerKind.Unrecognized"/> — never throws for a
    /// malformed or unreadable assembly.
    /// </summary>
    public PropertyInitializer GetInitializer(Type declaringType, string propertyName)
    {
        var location = declaringType.Assembly.Location;
        if (string.IsNullOrEmpty(location))
            return PropertyInitializer.Unrecognized;

        if (!indexesByPath.TryGetValue(location, out var index))
        {
            index = AssemblyInitializerIndex.TryCreate(location);
            indexesByPath[location] = index;
        }

        return index?.GetInitializer(declaringType, propertyName) ?? PropertyInitializer.Unrecognized;
    }

    public void Dispose()
    {
        foreach (var index in indexesByPath.Values)
            index?.Dispose();
        indexesByPath.Clear();
    }

    private sealed class AssemblyInitializerIndex : IDisposable
    {
        private readonly PEReader peReader;
        private readonly MetadataReader metadata;
        private readonly Dictionary<string, IReadOnlyDictionary<string, PropertyInitializer>> fieldsByTypeName = new(StringComparer.Ordinal);

        private AssemblyInitializerIndex(PEReader peReader, MetadataReader metadata)
        {
            this.peReader = peReader;
            this.metadata = metadata;
        }

        public static AssemblyInitializerIndex? TryCreate(string assemblyPath)
        {
            try
            {
                var peReader = new PEReader(File.OpenRead(assemblyPath));
                if (!peReader.HasMetadata)
                {
                    peReader.Dispose();
                    return null;
                }

                return new AssemblyInitializerIndex(peReader, peReader.GetMetadataReader());
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public PropertyInitializer GetInitializer(Type declaringType, string propertyName)
        {
            var typeName = declaringType.FullName;
            if (typeName is null)
                return PropertyInitializer.Unrecognized;

            if (!fieldsByTypeName.TryGetValue(typeName, out var fields))
            {
                fields = ParseType(typeName);
                fieldsByTypeName[typeName] = fields;
            }

            return fields.TryGetValue($"<{propertyName}>k__BackingField", out var initializer)
                ? initializer
                : PropertyInitializer.None;
        }

        public void Dispose() => peReader.Dispose();

        private IReadOnlyDictionary<string, PropertyInitializer> ParseType(string typeFullName)
        {
            try
            {
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var typeDefinition = metadata.GetTypeDefinition(handle);
                    if (!string.Equals(BuildFullName(typeDefinition), typeFullName, StringComparison.Ordinal))
                        continue;

                    return ParseConstructors(handle, typeDefinition);
                }
            }
            catch (BadImageFormatException)
            {
                // Fall through to the empty map: every property on this type reads as None, which the
                // caller's ladder resolves conservatively.
            }

            return new Dictionary<string, PropertyInitializer>(StringComparer.Ordinal);
        }

        private string BuildFullName(TypeDefinition typeDefinition)
        {
            var name = metadata.GetString(typeDefinition.Name);
            if (typeDefinition.IsNested)
            {
                var declaring = metadata.GetTypeDefinition(typeDefinition.GetDeclaringType());
                return $"{BuildFullName(declaring)}+{name}";
            }

            var ns = metadata.GetString(typeDefinition.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        private IReadOnlyDictionary<string, PropertyInitializer> ParseConstructors(TypeDefinitionHandle typeHandle, TypeDefinition typeDefinition)
        {
            var merged = new Dictionary<string, PropertyInitializer>(StringComparer.Ordinal);

            var constructors = typeDefinition.GetMethods()
                .Select(metadata.GetMethodDefinition)
                .Where(method => string.Equals(metadata.GetString(method.Name), ".ctor", StringComparison.Ordinal) &&
                                 method.RelativeVirtualAddress != 0);
            foreach (var method in constructors)
            {
                IReadOnlyDictionary<string, PropertyInitializer> parsed;
                try
                {
                    var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                    parsed = ParseBody(body.GetILBytes() ?? [], typeHandle);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
                {
                    continue;
                }

                foreach (var (field, initializer) in parsed)
                {
                    // Field initializers are duplicated into every non-chaining constructor; merge keeps the
                    // first classification and only downgrades Constant → Unrecognized when a sibling
                    // constructor disagrees (a hand-written conflicting assignment).
                    if (!merged.TryGetValue(field, out var existing))
                        merged[field] = initializer;
                    else if (existing.Kind == InitializerKind.Constant &&
                             (initializer.Kind != InitializerKind.Constant || !Equals(existing.Value, initializer.Value)))
                        merged[field] = PropertyInitializer.Unrecognized;
                }
            }

            return merged;
        }

        private IReadOnlyDictionary<string, PropertyInitializer> ParseBody(byte[] il, TypeDefinitionHandle owningType)
        {
            var results = new Dictionary<string, PropertyInitializer>(StringComparer.Ordinal);
            var offset = 0;

            // Mini pattern machine: ldarg.0 → constant [conv.*] [newobj] → stfld.
            var haveThis = false;
            var haveConstant = false;
            object? constant = null;
            var viaNewobj = false;
            var sawBranch = false;

            while (offset < il.Length)
            {
                var opCode = ReadOpCode(il, ref offset);

                if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
                {
                    sawBranch = true;
                    SkipOperand(opCode, il, ref offset);
                    haveThis = false;
                    haveConstant = false;
                    continue;
                }

                if (opCode == OpCodes.Ldarg_0)
                {
                    haveThis = true;
                    haveConstant = false;
                    viaNewobj = false;
                    continue;
                }

                if (TryReadConstant(opCode, il, ref offset, out var value))
                {
                    haveConstant = haveThis;
                    constant = value;
                    viaNewobj = false;
                    continue;
                }

                if (haveConstant && IsConversion(opCode))
                {
                    constant = ApplyConversion(opCode, constant);
                    if (constant is null)
                    {
                        // Unsupported/overflowing conversion breaks the pattern; the eventual stfld reads as Unrecognized.
                        haveThis = false;
                        haveConstant = false;
                    }

                    continue;
                }

                if (haveConstant && opCode == OpCodes.Newobj)
                {
                    // Tolerated for Nullable<T> wrapping (e.g. `int? X { get; set; } = 5;`). Whether the target
                    // really is Nullable<T> is decided by the caller from the property's CLR type; a newobj to
                    // any other ctor makes the constant meaningless there and resolves to Unrecognized.
                    viaNewobj = true;
                    offset += 4;
                    continue;
                }

                if (opCode == OpCodes.Stfld)
                {
                    var fieldName = ResolveOwnFieldName(ReadToken(il, ref offset), owningType);
                    if (fieldName is not null)
                    {
                        if (sawBranch)
                        {
                            // Post-branch stores are conditional: downgrade even a pre-branch constant.
                            results[fieldName] = PropertyInitializer.Unrecognized;
                        }
                        else if (!results.TryGetValue(fieldName, out var existing) || existing.Kind != InitializerKind.Unrecognized)
                        {
                            results[fieldName] = haveConstant
                                ? new PropertyInitializer(InitializerKind.Constant, constant, viaNewobj)
                                : PropertyInitializer.Unrecognized;
                        }
                    }

                    haveThis = false;
                    haveConstant = false;
                    viaNewobj = false;
                    continue;
                }

                SkipOperand(opCode, il, ref offset);
                haveThis = false;
                haveConstant = false;
                viaNewobj = false;
            }

            return results;
        }

        private string? ResolveOwnFieldName(int token, TypeDefinitionHandle owningType)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.FieldDefinition)
                return null;

            var field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
            return field.GetDeclaringType() == owningType ? metadata.GetString(field.Name) : null;
        }

        private bool TryReadConstant(OpCode opCode, byte[] il, ref int offset, out object? value)
        {
            if (opCode == OpCodes.Ldnull) { value = null; return true; }
            if (opCode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
            if (opCode == OpCodes.Ldc_I4_0) { value = 0; return true; }
            if (opCode == OpCodes.Ldc_I4_1) { value = 1; return true; }
            if (opCode == OpCodes.Ldc_I4_2) { value = 2; return true; }
            if (opCode == OpCodes.Ldc_I4_3) { value = 3; return true; }
            if (opCode == OpCodes.Ldc_I4_4) { value = 4; return true; }
            if (opCode == OpCodes.Ldc_I4_5) { value = 5; return true; }
            if (opCode == OpCodes.Ldc_I4_6) { value = 6; return true; }
            if (opCode == OpCodes.Ldc_I4_7) { value = 7; return true; }
            if (opCode == OpCodes.Ldc_I4_8) { value = 8; return true; }
            if (opCode == OpCodes.Ldc_I4_S) { value = (int)(sbyte)il[offset]; offset += 1; return true; }
            if (opCode == OpCodes.Ldc_I4) { value = BitConverter.ToInt32(il, offset); offset += 4; return true; }
            if (opCode == OpCodes.Ldc_I8) { value = BitConverter.ToInt64(il, offset); offset += 8; return true; }
            if (opCode == OpCodes.Ldc_R4) { value = BitConverter.ToSingle(il, offset); offset += 4; return true; }
            if (opCode == OpCodes.Ldc_R8) { value = BitConverter.ToDouble(il, offset); offset += 8; return true; }
            if (opCode == OpCodes.Ldstr)
            {
                var token = ReadToken(il, ref offset);
                value = metadata.GetUserString(MetadataTokens.UserStringHandle(token));
                return true;
            }

            value = null;
            return false;
        }

        private static bool IsConversion(OpCode opCode) =>
            opCode == OpCodes.Conv_I1 || opCode == OpCodes.Conv_U1 ||
            opCode == OpCodes.Conv_I2 || opCode == OpCodes.Conv_U2 ||
            opCode == OpCodes.Conv_I4 || opCode == OpCodes.Conv_U4 ||
            opCode == OpCodes.Conv_I8 || opCode == OpCodes.Conv_U8 ||
            opCode == OpCodes.Conv_R4 || opCode == OpCodes.Conv_R8;

        private static object? ApplyConversion(OpCode opCode, object? constant)
        {
            if (constant is not IConvertible convertible)
                return null;

            try
            {
                if (opCode == OpCodes.Conv_I1) return convertible.ToSByte(null);
                if (opCode == OpCodes.Conv_U1) return convertible.ToByte(null);
                if (opCode == OpCodes.Conv_I2) return convertible.ToInt16(null);
                if (opCode == OpCodes.Conv_U2) return convertible.ToUInt16(null);
                if (opCode == OpCodes.Conv_I4) return convertible.ToInt32(null);
                if (opCode == OpCodes.Conv_U4) return convertible.ToUInt32(null);
                if (opCode == OpCodes.Conv_I8) return convertible.ToInt64(null);
                if (opCode == OpCodes.Conv_U8) return convertible.ToUInt64(null);
                if (opCode == OpCodes.Conv_R4) return convertible.ToSingle(null);
                if (opCode == OpCodes.Conv_R8) return convertible.ToDouble(null);
            }
            catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException)
            {
                return null;
            }

            return null;
        }

        private static int ReadToken(byte[] il, ref int offset)
        {
            var token = BitConverter.ToInt32(il, offset);
            offset += 4;
            return token;
        }

        private static readonly Dictionary<short, OpCode> OpCodeMap = BuildOpCodeMap();

        private static Dictionary<short, OpCode> BuildOpCodeMap()
        {
            var map = new Dictionary<short, OpCode>();
            var opCodes = typeof(OpCodes)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(field => field.GetValue(null))
                .OfType<OpCode>();
            foreach (var opCode in opCodes)
                map[opCode.Value] = opCode;

            return map;
        }

        private static OpCode ReadOpCode(byte[] il, ref int offset)
        {
            short value = il[offset];
            offset += 1;
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[offset]);
                offset += 1;
            }

            return OpCodeMap.TryGetValue(value, out var opCode)
                ? opCode
                : throw new BadImageFormatException($"Unknown IL opcode 0x{value:X4}.");
        }

        /// <summary>Skips the operand of any instruction the pattern machine does not interpret.</summary>
        private static void SkipOperand(OpCode opCode, byte[] il, ref int offset)
        {
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                case OperandType.ShortInlineBrTarget:
                    offset += 1;
                    break;
                case OperandType.InlineVar:
                    offset += 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    offset += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, offset);
                    offset += 4 + count * 4;
                    break;
                default:
                    throw new BadImageFormatException($"Unsupported operand type '{opCode.OperandType}'.");
            }
        }
    }
}
