using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Indicates that a supplemental reader only repeats roots from the selected physical material store.</summary>
internal interface IRuntimeMaterialRetentionRootSource
{
    bool IsCoveredBy(IWorkflowExecutableStore material);
}
