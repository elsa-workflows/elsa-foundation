namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Reads committed kernel roots from the same physical backend that owns the material being collected.</summary>
internal interface IRuntimeKernelExecutableRetentionMaterial
{
    ValueTask<IReadOnlyCollection<string>> ListPinnedKernelExecutableIdsAsync(CancellationToken cancellationToken);
}
