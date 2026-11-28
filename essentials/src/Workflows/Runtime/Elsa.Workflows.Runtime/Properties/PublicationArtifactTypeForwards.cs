using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;

[assembly: TypeForwardedTo(typeof(InMemoryExecutableActivityTemplateStore))]
[assembly: TypeForwardedTo(typeof(InMemoryWorkflowExecutableSourceReferenceStore))]
[assembly: TypeForwardedTo(typeof(InMemoryWorkflowExecutableStore))]
[assembly: TypeForwardedTo(typeof(RuntimeRequirementChecker))]
[assembly: TypeForwardedTo(typeof(WorkflowArtifactClosureSerializer))]
[assembly: TypeForwardedTo(typeof(JsonRuntimeDurableValueStorageDriver))]
[assembly: TypeForwardedTo(typeof(RuntimeDurableValueStorageDriverRegistry))]
