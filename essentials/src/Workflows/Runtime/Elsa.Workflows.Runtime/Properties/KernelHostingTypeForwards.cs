using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Values;

[assembly: TypeForwardedTo(typeof(DefaultRuntimeSecretMask))]
[assembly: TypeForwardedTo(typeof(MemoryTestRunTargetStore))]
[assembly: TypeForwardedTo(typeof(PersistenceWorkflowExecutionPartitionAccessor))]
[assembly: TypeForwardedTo(typeof(RuntimeExecutionIdGenerator))]
[assembly: TypeForwardedTo(typeof(TestRunHeldDispatches))]
[assembly: TypeForwardedTo(typeof(TestRunTargetCleaner))]
[assembly: TypeForwardedTo(typeof(TestRunTargetDispatcher))]
[assembly: TypeForwardedTo(typeof(WorkflowExecutableStartSourceResolver))]
[assembly: TypeForwardedTo(typeof(WorkflowTestRunTransportServiceCollectionExtensions))]
