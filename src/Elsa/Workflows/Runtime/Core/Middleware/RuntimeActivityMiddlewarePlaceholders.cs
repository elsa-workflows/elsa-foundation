namespace Elsa.Workflows.Runtime.Core.Middleware;

public sealed class RuntimeActivityLoadStateMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivityInputEvaluationMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivityInvokeMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivityOutputCaptureMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivitySchedulingMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivityCheckpointMiddleware : ActivityRuntimeMiddlewareBase;

public sealed class RuntimeActivityPostCommitMiddleware : ActivityRuntimeMiddlewareBase;
