# Quickstart: Execution-Time JavaScript Expression Surfaces

Once this unit lands and `JavaScriptWorkflowsRuntimeFeature` is enabled, a workflow author can use these surfaces inside a *Run JavaScript* activity (or any JavaScript expression evaluated during activity execution).

## Read workflow identity

```javascript
var id   = getWorkflowInstanceId();      // runtime workflow execution id
var corr = getCorrelationId();           // current correlation id
var name = getWorkflowInstanceName();    // current instance name
var defId = getWorkflowDefinitionId();   // from the pinned executable
```

## Read inputs, variables, prior outputs

```javascript
var who = input.name;          // or getInput("name")
var g   = variables.greeting;  // or getVariable("greeting") / getGreeting()
var out = getOutputFrom("PreviousActivity", "Result");  // or getOutput("Result")
```

## Write variables (persisted durably)

```javascript
setVariable("status", "approved");   // function form
setStatus("approved");               // named setter (for a variable named 'status')
variables.status = "approved";       // direct assignment (copied back after evaluation)
```

After the activity completes, a later activity reading `variables.status` observes `"approved"`, and the value survives a durable-store reload — because the mutation folds into the same checkpoint-commit durable-value write-back used by non-script variable changes.

## Set correlation id / instance name

```javascript
setCorrelationId("order-123");         // folds into the activity-completed workflow-state change
setWorkflowInstanceName("Order 123");
```

## What changed vs. before

- Enabling `JavaScriptWorkflowsRuntimeFeature` no longer throws on first evaluation (the five processors no longer require an unregistered `IWorkflowExecutionContext`).
- Identity functions, named accessors, execution-time output accessors, and variable write-back — previously dead — now work.
- Materialization-time expressions (activity input bindings like `variables.greeting + input.name`) behave exactly as before.

## Verifying the fix (guardrail)

The new guardrail test enables the feature, resolves every registered `IScriptPreProcessor`/`IScriptPostProcessor`, and evaluates a script end-to-end — proving no processor re-orphans behind a missing dependency.
