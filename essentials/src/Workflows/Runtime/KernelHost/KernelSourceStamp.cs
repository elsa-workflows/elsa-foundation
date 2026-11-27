using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHost;

internal static class KernelSourceStamp
{
    internal static WorkflowPublishedStart Published(WorkflowExecutableSourceReference source, KernelServedSlot served, string? subjectId) =>
        new(source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion, source.DefinitionVersionId, source.ArtifactVersion,
            served.ActivationId, served.SlotId, served.SlotName, served.Revision, Digest(source), subjectId);

    internal static string Digest(WorkflowExecutableSourceReference source) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));
}
