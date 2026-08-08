using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Reads <c>[ConsumerContract("id")]</c> pins out of a built test assembly as metadata.
/// </summary>
/// <remarks>
/// Metadata rather than reflection because the gate must see every suite in the tree: execution-loading a
/// test assembly would drag in its xunit and product closure and would restrict the gate to whatever some
/// aggregator project happened to reference — exactly the way a new suite's claims would slip through
/// unnoticed.
/// </remarks>
public static class ClaimPinReader
{
    private const string AttributeName = "ConsumerContractAttribute";

    public static IReadOnlyList<ClaimPin> Read(string assemblyPath)
    {
        var pins = new List<ClaimPin>();
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return pins;

        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                foreach (var attributeHandle in method.GetCustomAttributes())
                {
                    if (ClaimId(reader, reader.GetCustomAttribute(attributeHandle)) is not { } claimId)
                        continue;

                    var typeName = reader.GetString(type.Namespace) is { Length: > 0 } ns
                        ? ns + "." + reader.GetString(type.Name)
                        : reader.GetString(type.Name);
                    pins.Add(new ClaimPin(claimId, $"{typeName}.{reader.GetString(method.Name)}", assemblyPath));
                }
            }
        }

        return pins;
    }

    private static string? ClaimId(MetadataReader reader, CustomAttribute attribute)
    {
        if (!IsConsumerContract(reader, attribute))
            return null;

        // Single string constructor argument: prolog (0x0001) then a SerString.
        var blob = reader.GetBlobReader(attribute.Value);
        return blob.Length >= 2 && blob.ReadUInt16() == 1 ? blob.ReadSerializedString() : null;
    }

    private static bool IsConsumerContract(MetadataReader reader, CustomAttribute attribute) =>
        attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => DeclaringTypeName(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent) == AttributeName,
            HandleKind.MethodDefinition => reader.GetString(
                reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()).Name) == AttributeName,
            _ => false
        };

    private static string? DeclaringTypeName(MetadataReader reader, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name),
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        _ => null
    };
}
