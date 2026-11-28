using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;

[assembly: TypeForwardedTo(typeof(IRecurringScheduleCalculator))]
[assembly: TypeForwardedTo(typeof(RecurringScheduleCalculator))]
[assembly: TypeForwardedTo(typeof(RecurringTriggerPumpOptions))]
[assembly: TypeForwardedTo(typeof(RecurringTriggerPumpTask))]
[assembly: TypeForwardedTo(typeof(WorkflowsRuntimePreparedStandaloneRecurringTriggersFeature))]
[assembly: TypeForwardedTo(typeof(WorkflowsRuntimePreparedStandaloneSourcePumpFeature))]
[assembly: TypeForwardedTo(typeof(WorkflowsRuntimeScheduleCalculationFeature))]
