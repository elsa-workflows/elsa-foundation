using System.Security.Cryptography;

namespace Elsa.Activities.Bpmn.Internal;

internal static class BpmnModelIdentity
{
    public static string Create(params string[] parts)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            foreach (var part in parts) { writer.Write(part.Length); foreach (var codeUnit in part) writer.Write((ushort)codeUnit); }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}
