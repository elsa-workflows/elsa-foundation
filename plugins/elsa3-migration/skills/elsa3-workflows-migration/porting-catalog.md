# Porting catalog: custom Elsa 3 code to Elsa 4

This catalog says how to port each kind of custom Elsa 3 code to Elsa 4, one case at a time, so that every component
is ported the same way, whoever does it. Read it on its own, or follow it from [SKILL.md](SKILL.md).

## How to use a case

1. Find the cases that apply to the component. Each case starts with **Applies when** and a **Search**: the text that
   finds the construct in Elsa 3 source. One component often needs several cases (a trigger with bookmarks, services
   and outputs): record them all in the migration plan.
2. Apply the cases **in catalog order**, 01 to 19. Later cases build on the shape the earlier ones give.
3. Follow the case's verdict:
   - **port**: rewrite the code like the **After (Elsa 4)** example, following the **Steps**. Every "after" example is
     an excerpt of a compiled, tested sample feature named `Acme`, so it is current Elsa 4 code. The names are
     illustrative: use your own.
   - **redesign**: Elsa 4 has no counterpart for this construct. The case names the Elsa 4 alternatives. Write no code
     that imitates the Elsa 3 behaviour; record the redesign in the plan and let the owner of the workflows decide.
   - **interview**: the right Elsa 4 shape depends on why the Elsa 3 code was written. The case gives the questions
     and, per answer, the Elsa 4 features that reach the goal. Record the answer; write no workaround code.
   - A case with two verdicts names the part each covers.
4. Port the component's tests too (case 19), then convert the workflows again and read the new report.

The "before" examples show typical Elsa 3 code. They are written for this catalog; your code may differ in detail.

---

## Case 01: Base class

**Verdict**: port

**Applies when**: a class derives from Elsa 3's `Activity`, `Activity<T>`, `CodeActivity` or `CodeActivity<T>`.

**Search**: `: CodeActivity`, `: Activity`, `: Activity<`

**Before (Elsa 3)**
```csharp
[Activity("Acme", "Invoicing", "Sends an invoice.")]
public class SendInvoice : CodeActivity<string>
{
    [Input(Description = "The customer to invoice.")]
    public Input<string> CustomerId { get; set; } = default!;

    [Input(Description = "The amount to invoice.")]
    public Input<decimal> Amount { get; set; } = default!;

    [Input(DefaultValue = "EUR", UIHint = InputUIHints.DropDown, Options = new[] { "EUR", "USD" })]
    public Input<string> Currency { get; set; } = default!;

    [Output] public Output<string> InvoiceCurrency { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var invoices = context.GetRequiredService<IInvoiceClient>();
        var currency = Currency.Get(context);
        var number = await invoices.SendAsync(CustomerId.Get(context), Amount.Get(context), currency, context.CancellationToken);
        context.Set(Result, number);
        context.Set(InvoiceCurrency, currency);
    }
}
```

**After (Elsa 4)** <!-- sample: case-01 -->
```csharp
// The Elsa 3 type name Acme.SendInvoice is this class's full name, so no marker is needed (case 2).
// The service comes in through the constructor (case 10).
public sealed class SendInvoice(IInvoiceClient invoices) : Activity<SendInvoiceResult>
{
    [ActivityInput(DisplayName = "Customer", Description = "The customer to invoice.")]
    [Required]
    public string CustomerId { get; set; } = null!;

    [ActivityInput(Description = "The amount to invoice.")]
    [Required]
    public decimal Amount { get; set; }

    [ActivityInput(DefaultValue = "EUR", UIHint = ActivityInputUIHints.Dropdown, Options = ["EUR", "USD"])]
    public string Currency { get; set; } = "EUR";

    protected override async ValueTask<ActivityTransition<SendInvoiceResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var invoiceNumber = await invoices.SendAsync(CustomerId, Amount, Currency, context.CancellationToken);
        return ActivityTransition.Complete(new SendInvoiceResult(invoiceNumber, Currency));
    }
}

public sealed record SendInvoiceResult(
    [property: Output(DisplayName = "Invoice number")] string InvoiceNumber,
    [property: Output(Key = "InvoiceCurrency")] string Currency);
```

**Steps**:
1. Derive from `Activity<TResult>` when the activity produces values, or from `Activity` when it produces none.
2. Gather everything the Elsa 3 activity wrote to `Result` and its `Output<T>` properties into one result record
   (case 04): the activity returns all its outputs at once.
3. Override `ExecuteAsync(ActivityExecutionContext context)` and return a transition (case 05).
4. An activity that waits for something from outside derives from a stateful base class instead (cases 08 and 09).

## Case 02: Type name and the exact-name rule

**Verdict**: port

**Applies when**: always: every custom activity needs its Elsa 3 type name matched, or the converted workflows cannot
find it.

**Search**: the activity's Elsa 3 type name as the report names it, for example `Acme.SendInvoice`: a class named
`SendInvoice` in namespace `Acme`; `[Activity("Acme"` on a class named `SendInvoice` (the second string argument of
the three-argument form is the category, not the type); `Type = "SendInvoice"` in an `[Activity]` attribute; or an
`[Activity]` attribute whose third positional argument is an integer version, such as `[Activity("Acme", "SendInvoice", 2)]`.

**Before (Elsa 3)**
```csharp
// Elsa 3 type name: Acme.SendInvoice (attribute namespace + class name).
[Activity("Acme", "Invoicing")]
public class SendInvoice : CodeActivity<string> { /* ... */ }

// Elsa 3 type name: Acme.Legacy.Notify-v2 (attribute namespace + Type).
[Activity("Acme.Legacy", "Notifications", Type = "Notify-v2")]
public class NotifyV2 : CodeActivity { /* ... */ }
```

**After (Elsa 4)** <!-- sample: case-02 -->
```csharp
namespace Acme.Legacy;

// The Elsa 3 type name Acme.Legacy.Notify-v2 cannot be a namespace and class (it holds a hyphen),
// so the activity names the Elsa 3 type it replaces. Any other Elsa 3 type name is matched by
// naming the namespace and class after it, with no marker (Acme.SendInvoice).
[ReplacesActivityType("Acme.Legacy.Notify-v2")]
public sealed class Notify : Activity
{
    [ActivityInput]
    [Required]
    public string Message { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
```

**Steps**:
1. The converter looks up the Elsa 3 type name as the full name of an Elsa 4 activity class. Name the namespace and
   class after the Elsa 3 type name (`Acme.SendInvoice` becomes class `SendInvoice` in namespace `Acme`). Nothing else
   is needed.
2. Only when the Elsa 3 type name cannot be a C# namespace and class (a hyphen, a leading digit, a reserved word), add
   `[ReplacesActivityType("<Elsa 3 type name>")]` with the exact Elsa 3 name.
3. Two Elsa 4 activities that claim the same Elsa 3 type make the conversion refuse; claim each Elsa 3 type once.

## Case 03: Inputs (defaults, UI hints, required)

**Verdict**: port

**Applies when**: the activity has `Input<T>` properties with an `[Input]` attribute.

**Search**: `Input<`, `[Input(`

**Before (Elsa 3)**
```csharp
[Input(Description = "The customer to invoice.")]
public Input<string> CustomerId { get; set; } = default!;

[Input(Description = "The amount to invoice.")]
public Input<decimal> Amount { get; set; } = default!;

[Input(DefaultValue = "EUR", UIHint = InputUIHints.DropDown, Options = new[] { "EUR", "USD" })]
public Input<string> Currency { get; set; } = default!;
```

**After (Elsa 4)** <!-- sample: case-03 -->
```csharp
[ActivityInput(DisplayName = "Customer", Description = "The customer to invoice.")]
[Required]
public string CustomerId { get; set; } = null!;

[ActivityInput(Description = "The amount to invoice.")]
[Required]
public decimal Amount { get; set; }

[ActivityInput(DefaultValue = "EUR", UIHint = ActivityInputUIHints.Dropdown, Options = ["EUR", "USD"])]
public string Currency { get; set; } = "EUR";
```

**Steps**:
1. Turn each `Input<T>` into a plain property of type `T` with `[ActivityInput]`. Read it as an ordinary property;
   there is no `.Get(context)`.
2. Move `Description`, `DisplayName`, `Category`, `DefaultValue`, `UIHint` and `Options` onto `[ActivityInput]`. Give the
   property the same default in C# as in `DefaultValue`.
3. Mark inputs the activity cannot run without with `[Required]`.
4. An Elsa 3 `UIHandler` or `DefaultValueProvider` becomes `OptionsProvider` on `[ActivityInput]`, or a static default.
   Decide per input.
5. Keep the input names: the converted workflows bind values to them by name.

## Case 04: Outputs

**Verdict**: port

**Applies when**: the activity sets `Result` or has `Output<T>` properties.

**Search**: `Output<`, `[Output]`, `context.Set(Result`, `Result.Set(`

**Before (Elsa 3)**
```csharp
public class SendInvoice : CodeActivity<string>   // Result holds the invoice number
{
    [Output] public Output<string> InvoiceCurrency { get; set; } = default!;
    // ...
    context.Set(Result, number);
    context.Set(InvoiceCurrency, currency);
}
```

**After (Elsa 4)** <!-- sample: case-04 -->
```csharp
public sealed record SendInvoiceResult(
    [property: Output(DisplayName = "Invoice number")] string InvoiceNumber,
    [property: Output(Key = "InvoiceCurrency")] string Currency);
```

**Steps**:
1. Write one record with a property per output, each marked `[Output]`. The activity returns it in its completion
   (case 05).
2. Keep the Elsa 3 output names where the converted workflows read them: give `[Output]` a `Key` when the property name
   differs.
3. Mark an output that may be absent with `IsRequired = false`.

## Case 05: Completion

**Verdict**: port

**Applies when**: the activity calls `CompleteActivityAsync`, or is a `CodeActivity` that completed on its own.

**Search**: `CompleteActivityAsync`, `: CodeActivity`

**Before (Elsa 3)**
```csharp
protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
{
    var number = await invoices.SendAsync(/* ... */);
    context.Set(Result, number);
    await context.CompleteActivityAsync();
}
```

**After (Elsa 4)** <!-- sample: case-05 -->
```csharp
protected override async ValueTask<ActivityTransition<SendInvoiceResult>> ExecuteAsync(ActivityExecutionContext context)
{
    var invoiceNumber = await invoices.SendAsync(CustomerId, Amount, Currency, context.CancellationToken);
    return ActivityTransition.Complete(new SendInvoiceResult(invoiceNumber, Currency));
}
```

**Steps**:
1. Return `ActivityTransition.Complete(result)` with the result record. An activity without a result returns
   `ActivityTransition.Complete(ActivityUnit.Value)`.
2. The activity completes with the outcome `Done` unless it names another (case 06).

## Case 06: Outcomes

**Verdict**: port

**Applies when**: the activity declares outcomes and completes with one of them.

**Search**: `[FlowNode(`, `CompleteActivityWithOutcomesAsync`, `Outcomes`

**Before (Elsa 3)**
```csharp
[FlowNode("Approved", "Rejected")]
public class Approve : Activity
{
    [Input] public Input<decimal> Amount { get; set; } = default!;
    [Input(DefaultValue = 1000)] public Input<decimal> Limit { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var outcome = Amount.Get(context) <= Limit.Get(context) ? "Approved" : "Rejected";
        await context.CompleteActivityWithOutcomesAsync(outcome);
    }
}
```

**After (Elsa 4)** <!-- sample: case-06 -->
```csharp
[ActivityOutcome("Approved")]
[ActivityOutcome("Rejected")]
public sealed class Approve : Activity
{
    [ActivityInput]
    public decimal Amount { get; set; }

    [ActivityInput(DefaultValue = "1000")]
    public decimal Limit { get; set; } = 1000m;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, Amount <= Limit ? "Approved" : "Rejected"));
}
```

**Steps**:
1. Declare each outcome with `[ActivityOutcome("<name>")]` on the class, with the same names as in Elsa 3: the
   converted workflows connect to them by name.
2. Name the outcome in the completion: `ActivityTransition.Complete(result, "<name>")`.

## Case 07: Scheduling child activities

**Verdict**: redesign

**Applies when**: the activity holds child activities and schedules them: a custom composite, loop or container.

**Search**: `[Port]`, `ScheduleActivityAsync`, `ScheduleActivitiesAsync`, `CompletionCallback`

**Before (Elsa 3)**
```csharp
public class RetryBlock : Activity
{
    [Port] public IActivity? Body { get; set; }
    [Input] public Input<int> MaxAttempts { get; set; } = new(3);

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context) =>
        await context.ScheduleActivityAsync(Body, OnBodyCompletedAsync);

    private async ValueTask OnBodyCompletedAsync(ActivityCompletedContext context) =>
        await context.TargetContext.CompleteActivityAsync();
}
```

**Redesign**: Elsa 4 has no author surface for scheduling child activities: structural activities belong to the
engine. Do not port the class. Instead, before porting anything that depends on it, choose one of:
- a **reusable Activity Graph definition**: a workflow usable as an activity, built from Elsa 4's own activities, that
  does what the composite did;
- the **built-in composites**: `Sequence`, `Flowchart`, `If`, `Switch`, `ForEach`, `For`, `While`, `Do` and `Parallel`,
  arranged in each workflow that used the composite.

Record the choice in the plan's component row (status `redesign`) and change the Elsa 3 workflows or rebuild them in
Elsa 4, as the owner decides.

## Case 08: Signals and bookmarks

**Verdict**: port (bookmarks); redesign (signals)

**Applies when**: the activity waits for something from outside (a bookmark), or sends or receives signals.

**Search**: `CreateBookmark`, `CreateBookmarks`, `BookmarkPayload`, `SendSignalAsync`, `OnSignalReceived`

**Before (Elsa 3)**
```csharp
public class WaitForPayment : Activity<decimal>
{
    [Input] public Input<string> InvoiceNumber { get; set; } = default!;

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        context.CreateBookmark(new PaymentBookmarkPayload(InvoiceNumber.Get(context)), OnPaymentReceivedAsync);
        return default;
    }

    private async ValueTask OnPaymentReceivedAsync(ActivityExecutionContext context)
    {
        context.Set(Result, context.GetWorkflowInput<decimal>("Amount"));
        await context.CompleteActivityAsync();
    }
}
```

**After (Elsa 4)** <!-- sample: case-08 -->
```csharp
// Elsa 3 created a bookmark and resumed on it; Elsa 4 suspends with typed private state and a trigger
// registration, and resumes on the method marked with the registration's resume target.
public sealed class WaitForPayment : StatefulActivity<PaymentResult, PaymentWaitState, PaymentReceived>
{
    public const string StimulusType = "Acme.Payment";
    private const string PaymentTarget = "resume-target:payment";

    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override ValueTask<ActivityTransition<PaymentResult, PaymentWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        var registration = new ActivityTriggerRegistration<PaymentReceived>(PaymentTarget, StimulusType, $"invoice:{InvoiceNumber}");
        return ValueTask.FromResult(Suspend(new PaymentWaitState(InvoiceNumber), [registration]));
    }

    [ResumeTarget(PaymentTarget)]
    protected override ValueTask<ActivityTransition<PaymentResult, PaymentWaitState>> ResumeAsync(
        ActivityResumeContext<PaymentWaitState, PaymentReceived> context) =>
        ValueTask.FromResult(Complete(new PaymentResult(context.State.InvoiceNumber, context.Trigger.Amount)));
}

public sealed record PaymentWaitState(string InvoiceNumber);

public sealed record PaymentReceived(decimal Amount);

public sealed record PaymentResult(
    [property: Output] string InvoiceNumber,
    [property: Output] decimal AmountPaid);
```

**Steps** (bookmarks):
1. Derive from `StatefulActivity<TResult, TState, TTrigger>`: `TState` holds what the activity must remember while it
   waits; `TTrigger` is the payload that resumes it.
2. In `ExecuteAsync`, return `Suspend(state, [registration])`. The registration names a resume target, a stimulus type
   and a stimulus hash: the identity a delivery must match. Derive the hash from what the Elsa 3 bookmark payload held.
3. Implement `ResumeAsync`, mark it `[ResumeTarget("<the registration's resume target>")]`, and complete from the state
   and the trigger.
4. The code that resumed the Elsa 3 bookmark now delivers a stimulus of that type and hash to the Elsa 4 runtime.

**Redesign** (signals): signals between activities are engine-only in Elsa 4. Model the exchange as workflow data
(variables and outcomes) or as a stimulus the waiting activity subscribes to, and record the redesign in the plan.

## Case 09: Triggers

**Verdict**: port

**Applies when**: the activity starts workflows: an Elsa 3 `Trigger`, or an activity with a trigger payload.

**Search**: `: Trigger`, `GetTriggerPayload`, `GetTriggerPayloads`, `IsTriggerOfWorkflow`

**Before (Elsa 3)**
```csharp
public class InvoiceReceived : Trigger<string>
{
    [Input] public Input<string> Mailbox { get; set; } = default!;

    protected override object GetTriggerPayload(TriggerIndexingContext context) =>
        new InvoiceStimulus(Mailbox.Get(context.ExpressionExecutionContext));

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.IsTriggerOfWorkflow())
        {
            context.Set(Result, context.GetWorkflowInput<string>("InvoiceNumber"));
            await context.CompleteActivityAsync();
            return;
        }

        context.CreateBookmark(new InvoiceStimulus(Mailbox.Get(context)), OnInvoiceAsync);
    }

    private async ValueTask OnInvoiceAsync(ActivityExecutionContext context)
    {
        context.Set(Result, context.GetWorkflowInput<string>("InvoiceNumber"));
        await context.CompleteActivityAsync();
    }
}
```

**After (Elsa 4)** <!-- sample: case-09 -->
```csharp
// A trigger activity: it completes when an incoming invoice starts the workflow at this node, and otherwise
// waits for the next one. The stimulus provider below tells publishing which stimulus starts the workflow;
// AcmeFeature registers it.
[TriggerActivity]
public sealed class InvoiceReceived : StatefulTriggerActivity<InvoiceReceivedResult, InvoiceWaitState, IncomingInvoice>
{
    public const string ActivityType = "Acme.InvoiceReceived";
    public const string StimulusType = "Acme.Invoice";
    private const string InvoiceTarget = "resume-target:invoice";

    [ActivityInput]
    [Required]
    public string Mailbox { get; set; } = null!;

    public static string Hash(string mailbox) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mailbox))).ToLowerInvariant()}";

    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceWaitState>> ExecuteAsync(
        ActivityStartContext<IncomingInvoice> context)
    {
        if (context.TryGetTrigger(out var invoice))
            return ValueTask.FromResult(Complete(new InvoiceReceivedResult(invoice.InvoiceNumber)));

        var registration = new ActivityTriggerRegistration<IncomingInvoice>(InvoiceTarget, StimulusType, Hash(Mailbox));
        return ValueTask.FromResult(Suspend(new InvoiceWaitState(Mailbox), [registration]));
    }

    [ResumeTarget(InvoiceTarget)]
    protected override ValueTask<ActivityTransition<InvoiceReceivedResult, InvoiceWaitState>> ResumeAsync(
        ActivityResumeContext<InvoiceWaitState, IncomingInvoice> context) =>
        ValueTask.FromResult(Complete(new InvoiceReceivedResult(context.Trigger.InvoiceNumber)));
}

public sealed record IncomingInvoice(string InvoiceNumber);

public sealed record InvoiceWaitState(string Mailbox);

public sealed record InvoiceReceivedResult([property: Output] string InvoiceNumber);

public sealed class InvoiceReceivedStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => InvoiceReceived.ActivityType;

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        if (!StringComparer.Ordinal.Equals(node.ActivityType, InvoiceReceived.ActivityType))
            return ActivityTriggerStimulusResult.NotRecognized;

        // The stimulus is fixed at publish time, so the mailbox must be a literal.
        var binding = node.InputBindings.GetValueOrDefault(nameof(InvoiceReceived.Mailbox));
        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { ValueKind: JsonValueKind.String } mailbox)
            throw new ArgumentException($"Trigger node '{node.ExecutableNodeId}' needs a literal mailbox.");

        var stimulus = new TriggerStimulusDescriptor(InvoiceReceived.StimulusType, InvoiceReceived.Hash(mailbox.GetString()!));
        return ActivityTriggerStimulusResult.Recognized([stimulus]);
    }
}
```

**Steps**:
1. Mark the class `[TriggerActivity]` and derive from `StatefulTriggerActivity<TResult, TState, TTrigger>`.
2. In `ExecuteAsync`, `context.TryGetTrigger(out var trigger)` is true when a delivery started the workflow at this
   node: complete from it. Otherwise suspend as in case 08.
3. Write an `IActivityTriggerStimulusProvider` that recognises the activity type and describes the stimulus from the
   node's literal inputs, the way `GetTriggerPayload` did. Register it in the feature (case 18).

## Case 10: Services

**Verdict**: port

**Applies when**: the activity asks the context for a service.

**Search**: `GetRequiredService<`, `GetService<`, `context.GetServices`

**Before (Elsa 3)**
```csharp
protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
{
    var invoices = context.GetRequiredService<IInvoiceClient>();
    context.Set(Result, await invoices.GetStatusAsync(InvoiceNumber.Get(context), context.CancellationToken));
}
```

**After (Elsa 4)** <!-- sample: case-10 -->
```csharp
// Elsa 3 asked the context for the service (context.GetRequiredService<IInvoiceClient>()).
// Elsa 4 passes it to the constructor; the execution context offers no service lookup.
public sealed class CheckInvoiceStatus(IInvoiceClient invoices) : Activity<InvoiceStatusResult>
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override async ValueTask<ActivityTransition<InvoiceStatusResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ActivityTransition.Complete(new InvoiceStatusResult(await invoices.GetStatusAsync(InvoiceNumber, context.CancellationToken)));
}

public sealed record InvoiceStatusResult([property: Output] string Status);
```

**Steps**:
1. Take each service as a constructor parameter. The execution context offers no service lookup.
2. Register the service in the feature (case 18).

## Case 11: Variables and workflow state

**Verdict**: port; redesign (the properties bag)

**Applies when**: the activity reads or writes workflow variables, workflow outputs, the correlation id or the
instance name, or keeps data in the workflow's properties bag.

**Search**: `Variable<`, `.Get(context)` on a variable, `GetVariable`, `SetVariable`, `WorkflowExecutionContext.Output`,
`CorrelationId`, `WorkflowExecutionContext.Properties`, `TransientProperties`

**Before (Elsa 3)**
```csharp
public class AddToInvoiceTotal : CodeActivity
{
    [Input] public Input<decimal> Amount { get; set; } = default!;
    public Variable<decimal> Total { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context) =>
        Total.Set(context, Total.Get(context) + Amount.Get(context));
}

public class ReadCustomerNote : CodeActivity<string>
{
    protected override void Execute(ActivityExecutionContext context) =>
        context.Set(Result, context.GetVariable<string>("customerNote"));
}

public class PublishInvoiceNumber : CodeActivity
{
    [Input] public Input<string> InvoiceNumber { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context) =>
        context.WorkflowExecutionContext.Output["invoiceNumber"] = InvoiceNumber.Get(context);
}
```

**After (Elsa 4)** <!-- sample: case-11 -->
```csharp
// A variable the author picks: the input is a variable reference, read and written through the step's variables.
public sealed class AddToInvoiceTotal : Activity
{
    [ActivityInput]
    [Required]
    public VariableReference<decimal> Total { get; set; } = null!;

    [ActivityInput]
    public decimal Amount { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Total, context.Variables.Get(Total) + Amount);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}

// A variable read by a name the workflow does not pass in: the activity declares that it does so.
[AccessesUndeclaredVariables]
public sealed class ReadCustomerNote : Activity<CustomerNoteResult>
{
    protected override ValueTask<ActivityTransition<CustomerNoteResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new CustomerNoteResult(context.Variables.Get<string>("customerNote"))));
}

public sealed record CustomerNoteResult([property: Output] string Note);

// Workflow state (here, a workflow output): the activity declares that it accesses it.
[AccessesWorkflowState]
public sealed class PublishInvoiceNumber : Activity
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.WorkflowState.SetOutput("invoiceNumber", JsonSerializer.SerializeToElement(InvoiceNumber));
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
```

**Steps**:
1. A variable the workflow author chooses becomes a `VariableReference<T>` input (or `ReadOnlyVariableReference<T>`
   when the activity only reads it). Read and write it with `context.Variables.Get(reference)` and
   `context.Variables.Set(reference, value)`.
2. A variable read or written by a fixed name the workflow does not pass in: mark the class
   `[AccessesUndeclaredVariables]` and use `context.Variables.Get<T>("name")` or `Set`.
3. Workflow outputs, the correlation id and the instance name: mark the class `[AccessesWorkflowState]` and use
   `context.WorkflowState`.

**Redesign** (the properties bag): `WorkflowExecutionContext.Properties` and `TransientProperties` have no Elsa 4
counterpart. Keep the data in a declared workflow variable or pass it as an activity output, and record the redesign.

## Case 12: Background and task activities

**Verdict**: port (when the activity meets the eligibility criteria below; otherwise it runs in the foreground)

**Applies when**: the Elsa 3 activity ran in the background: a `Task` or `Job` kind, or `RunAsynchronously`.

**Search**: `ActivityKind.Task`, `ActivityKind.Job`, `Kind = ActivityKind`, `RunAsynchronously`

**Before (Elsa 3)**
```csharp
[Activity("Acme", "Invoicing", Kind = ActivityKind.Task)]
public class ArchiveInvoice : CodeActivity
{
    [Input] public Input<string> InvoiceNumber { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var invoices = context.GetRequiredService<IInvoiceClient>();
        await invoices.ArchiveAsync(InvoiceNumber.Get(context), context.Id, context.CancellationToken);
    }
}
```

**After (Elsa 4)** <!-- sample: case-12 -->
```csharp
// Safe to run in the background: it reads only its inputs, honours the cancellation token, and keys its
// effect on LogicalInvocationId, which stays the same when an operator retries the step (InvocationId does not).
// The workflow author chooses whether a step runs in the background.
[BackgroundExecutionSafe]
public sealed class ArchiveInvoice(IInvoiceClient invoices) : Activity
{
    [ActivityInput]
    [Required]
    public string InvoiceNumber { get; set; } = null!;

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        await invoices.ArchiveAsync(InvoiceNumber, context.LogicalInvocationId, context.CancellationToken);
        return ActivityTransition.Complete(ActivityUnit.Value);
    }
}
```

**Steps**:
1. Check the eligibility criteria: the activity works only from its inputs and returns a result (no variable session,
   no workflow state: no `[AccessesUndeclaredVariables]`, `[AccessesWorkflowState]` or variable-reference input); it
   does not suspend; it honours `context.CancellationToken`; and an effect that must not happen twice is keyed on
   `context.LogicalInvocationId` (or `context.OperationKey`), which an operator retry keeps. `InvocationId` changes on a
   retry.
2. When it meets them, mark the class `[BackgroundExecutionSafe]`. When it does not, leave the marker off: the activity
   runs in the foreground, and the plan notes it.
3. The marker only allows background placement. Each workflow author chooses whether a step runs in the background, so
   the converted workflows run it in the foreground until someone chooses otherwise.

## Case 13: Cancellation and faults

**Verdict**: port

**Applies when**: the activity throws to report failure, cancels itself, or reacts to cancellation.

**Search**: `throw new`, `CancelActivityAsync`, `CancellationToken`, `Fault`, `OnCanceledAsync`

**Before (Elsa 3)**
```csharp
protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
{
    if (context.CancellationToken.IsCancellationRequested)
    {
        await context.CancelActivityAsync();
        return;
    }

    if (CardBlocked.Get(context))
        throw new InvalidOperationException("The card is blocked.");   // became an incident

    context.Set(Result, Amount.Get(context));
}
```

**After (Elsa 4)** <!-- sample: case-13 -->
```csharp
// Elsa 3 threw (an incident) or reacted to a cancel signal. Elsa 4 returns a fault or a cancel transition,
// and observes cancellation through the context's token; there is no per-activity cancel callback.
public sealed class ChargeCard : Activity<ChargeResult>
{
    [ActivityInput]
    public decimal Amount { get; set; }

    [ActivityInput]
    public bool CardBlocked { get; set; }

    protected override ValueTask<ActivityTransition<ChargeResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
            return ValueTask.FromResult(ActivityTransition.Cancel<ChargeResult>("The charge was cancelled before it started."));

        if (CardBlocked)
            return ValueTask.FromResult(ActivityTransition.Fault<ChargeResult>(
                new ActivityFault("acme.card-blocked", "The card is blocked.", isRetryable: false)));

        return ValueTask.FromResult(ActivityTransition.Complete(new ChargeResult(Amount)));
    }
}

public sealed record ChargeResult([property: Output] decimal Charged);
```

**Steps**:
1. Return `ActivityTransition.Fault<TResult>(new ActivityFault(code, message, isRetryable))` for a failure the activity
   knows about. Give it a stable code of your own; say whether a retry can help.
2. Return `ActivityTransition.Cancel<TResult>(reason)` to cancel.
3. Pass `context.CancellationToken` to everything that can wait. There is no per-activity cancel callback.
4. An unexpected exception still faults the step; do not catch it only to rethrow it.

## Case 14: JavaScript functions

**Verdict**: port (effect-free functions with JSON in and out); redesign (functions with effects: as an activity)

**Applies when**: the Elsa 3 host added a global function to its JavaScript engine, or the report names a
JavaScript function as a component.

**Search**: `SetValue("<function name>"`, `ConfigureEngine`, `EvaluatingJavaScript`

**Before (Elsa 3)**
```csharp
services.Configure<JintOptions>(options => options.ConfigureEngine(engine =>
    engine.SetValue("lookupCustomer", (Func<string, object?>)(id => customers.Find(id)))));

// In a workflow: lookupCustomer(getCustomerId()).name
```

**After (Elsa 4)** <!-- sample: case-14 -->
```csharp
// The Elsa 3 global lookupCustomer(id) becomes acme.lookupCustomer(id): the converter matches a global to the
// registered host function with the same name after its prefix, and rewrites direct calls. The function is
// effect-free and takes and returns JSON. AcmeFeature registers the declaration.
public static class AcmeScriptFunctions
{
    private static readonly Dictionary<string, string> CustomerNames = new(StringComparer.Ordinal)
    {
        ["c-1"] = "Acme Retail",
        ["c-2"] = "Acme Wholesale"
    };

    public static JavaScriptHostFunctionDeclaration LookupCustomer { get; } = new("acme.lookupCustomer", call =>
    {
        var id = call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.String ? call.Arguments[0].GetString()! : "";
        return CustomerNames.TryGetValue(id, out var name)
            ? JsonSerializer.SerializeToElement(new { id, name })
            : JsonSerializer.SerializeToElement<object?>(null);
    });
}
```

**Steps**:
1. Choose a prefix (a namespace for your functions, such as `acme`) and record it in the plan. Elsa 4 host functions
   always carry one: `acme.lookupCustomer`.
2. Declare the function as a `JavaScriptHostFunctionDeclaration` named `<prefix>.<Elsa 3 global name>`. It takes JSON
   arguments and returns JSON. Set `ReadsVariables` when it reads workflow variables the script does not name.
3. Register the declaration as a singleton in the feature (case 18).
4. Convert the workflows again: the converter rewrites each direct call, `lookupCustomer(x)` becomes
   `acme.lookupCustomer(x)`.

The conversion's rules, as they stand:
- **Matching.** A global matches the registered host function with the same name after its prefix. No mapping is
  declared.
- **Ambiguous.** The match is refused when two prefixes carry the same name, when a variable, input or activity output
  has a generated accessor with that name, or when a script declares the prefix itself.
- **Never matched.** Functions the converter converts itself keep that conversion: the variable and input readers
  and setters (`getVariable`, `setVariable`, the generated `get…` and `set…` accessors), `toJson`, the Elsa 4
  built-ins such as `newGuid` and `isNullOrEmpty`, the outcome setters, the instance-state helpers (including the
  instance-name readers), the read roots (`variables`, `input`, `activities`, `workflow`, `args`), the CLR, lodash and
  moment globals, and standard JavaScript globals such as `String` or `encodeURIComponent`.
- **Direct calls only.** The converter rewrites direct calls. A use other than a direct call (`const f =
  lookupCustomer`, `items.map(lookupCustomer)`) is refused once a host function provides the name. Make it a direct
  call in the Elsa 3 definition first: `items.map(item => lookupCustomer(item))`.

Elsa 3 built-ins a host function may take over: Elsa 4 does not provide these, so the converter refuses them, unless
your host registers a function of the same name with a prefix: `getConfig`, `getConfiguration`, the definition
readers (`getWorkflowDefinitionId`, `getWorkflowDefinitionVersionId`, `getWorkflowDefinitionVersion`),
`getLastResult`, and the byte and stream helpers (`bytesToString`, `bytesFromString`, `bytesToBase64`,
`bytesFromBase64`, `streamToBytes`, `streamToBase64`). They are Elsa 3's own, so there is no Elsa 3 source to search
for them. Port one only when the workflows need it; passing the value in as a workflow input is often simpler.

**Redesign** (functions with effects): an Elsa 4 host function must be effect-free. A function that sends, writes or
changes anything becomes an activity (cases 01 to 05), and the workflows call that activity in a step of its own.

## Case 15: Liquid filters and other expression additions

**Verdict**: redesign (Liquid filters); a new expression syntax is named with its extension points but has no example

**Applies when**: the Elsa 3 host added a Liquid filter, or a whole expression language.

**Search**: `ILiquidFilter`, `AddLiquidFilter`, `IExpressionHandler`, `IExpressionDescriptorProvider`

**Before (Elsa 3)**
```csharp
public class CurrencyFilter : ILiquidFilter
{
    public ValueTask<FluidValue> ProcessAsync(FluidValue input, FilterArguments arguments, LiquidTemplateContext context) =>
        new(new StringValue($"{input.ToNumberValue():0.00} EUR"));
}

services.AddLiquidFilter<CurrencyFilter>("currency");
```

**Redesign**: Elsa 4 does not let modules register Liquid filters. Choose one of:
- a JavaScript host function (case 14) for an effect-free calculation, used from a JavaScript expression instead of the
  Liquid template;
- an activity (cases 01 to 05) for anything else, whose output the template then reads.

A whole new expression syntax is possible through the public extension points `IExpressionDescriptorProvider` and
`IPortableExpressionHandler`. That is a project of its own; this catalog gives no example.

## Case 16: Variable type registration

**Verdict**: port

**Applies when**: the Elsa 3 host registered a variable type, or the report names a variable type as a component.

**Search**: the alias in `AddVariableTypeAndAlias<` or `AddTypeAlias<` (with no alias argument, the alias is the class
name); for a type registered with `AddVariableType<` or `AddVariableTypes(`, the export holds the assembly-qualified
name, so search for the namespace and class before the comma.

**Before (Elsa 3)**
```csharp
elsa.UseWorkflowManagement(management => management.AddVariableTypeAndAlias<Invoice>("Invoice", "Acme"));

// In the export: { "name": "currentInvoice", "typeName": "Invoice", ... }
```

**After (Elsa 4)** <!-- sample: case-16 -->
```csharp
public sealed record Invoice(string Number, decimal Amount, string Currency);

// Registers Invoice under the dotted alias acme.Invoice. Elsa 4 module aliases are dotted, so a variable typed
// with the bare Elsa 3 alias Invoice is changed to acme.Invoice in the Elsa 3 definition before conversion.
public sealed class AcmeVariableTypes : IVariableTypeDescriptorProvider
{
    public const string InvoiceAlias = "acme.Invoice";

    public IEnumerable<TypeDescriptor> GetDescriptors()
    {
        yield return new TypeDescriptor(
            InvoiceAlias,
            typeof(Invoice),
            "Invoice",
            "Acme",
            "json",
            new HashSet<CollectionKind> { CollectionKind.Single },
            SupportsNull: true,
            SupportsDurability: true,
            new HashSet<string>(StringComparer.Ordinal) { "elsa.json" });
    }
}
```

**Steps**:
1. Implement `IVariableTypeDescriptorProvider` and register the type under a dotted alias, `<prefix>.<Name>`. Elsa 4
   refuses a module alias without a dot.
2. Register the provider in the feature (case 18).
3. How the converter matches: it looks up the Elsa 3 type name as an Elsa 4 alias, or as the full name of a registered
   type, and otherwise loads it as an assembly-qualified CLR name. A bare Elsa 3 alias such as `Invoice` therefore
   matches nothing. Either change the variable's type in the Elsa 3 definition to the Elsa 4 alias (`acme.Invoice`)
   before converting, or keep the variable untyped (it converts as Any) and record the component as dropped in the
   plan.
4. A variable type exported under its assembly-qualified name matches when the Elsa 4 type has the same full name.

## Case 17: Storage drivers

**Verdict**: interview

**Applies when**: the report names a storage driver as a component: Elsa 3's memory driver, or a custom driver.

**Search**: for a custom driver, the type name before the comma in its assembly-qualified name, as the report names it
(`Acme.BlobStorageDriver, Acme` → class `BlobStorageDriver` in namespace `Acme`), and `IStorageDriver`,
`AddStorageDriver`. Elsa 3's own drivers (namespace `Elsa.`) need no search.

**Before (Elsa 3)**
```csharp
public class BlobStorageDriver : IStorageDriver
{
    public ValueTask WriteAsync(string id, object value, StorageDriverContext context) { /* writes a blob */ }
    public ValueTask<object?> ReadAsync(string id, StorageDriverContext context) { /* reads it back */ }
    public ValueTask DeleteAsync(string id, StorageDriverContext context) { /* deletes it */ }
}

services.AddStorageDriver<BlobStorageDriver>();

// In the export: { "name": "document", "storageDriverTypeName": "Acme.BlobStorageDriver, Acme", ... }
```

**What the conversion does**: every variable, input and output becomes Elsa 4's default storage: its value is
stored with the workflow instance. Elsa 3's default drivers need nothing. A member on the memory driver (Elsa 3 never
persisted it) or on a custom driver (Elsa 3 kept the value elsewhere) converts with a Reconsider finding, because
somebody chose that driver on purpose and Elsa 4 now behaves differently.

**Interview**: hold it once per driver type, not once per member: list every variable, input and output the report
flags for that driver and go through them together. Record the answers in the plan's row for the driver.

1. **What did the driver do for you?** Ask in the owner's own words, then match the answer to one or more goals:
   - **A. Never persist a secret** (an API token or password held for a few steps).
   - **B. Never persist bulky or short-lived data** (a downloaded document, a large response, an intermediate result).
   - **C. Keep the value in another store** (a blob container, for size, retention, compliance or other readers).
   - **D. No particular reason** (copied from another workflow, a team default).
2. **Goal A, a secret.** Elsa 4 has no variable that skips persistence; do not keep the secret in a variable. Store it
   with the Secrets module and bind each activity input that needs it to a `Secret` reference; the `SecretsWorkflows`
   feature resolves it each time the activity runs, and the instance keeps only a withheld marker. Only an input that
   accepts a secret reference can take one: a script, a log line or Set Variable cannot read it, so `"Bearer " + token`
   is not possible. If the workflow combined the secret with other values, move that into an activity whose credential
   input takes the secret reference. Marking an input sensitive hides it in views and logs but still persists it.
3. **Goal B, bulky or short-lived data.** Fetch, use and drop the data inside one activity that returns only what later
   steps need (a status, an id, a summary); what stays inside the activity is not persisted, as long as the activity
   does not suspend in between. If later steps need the data itself, keep a reference (a URL, a blob name, a document
   id) in the variable and read the data through it. Ask how large the value gets and how often the workflow
   suspends: a small value may be fine in the default storage after all.
4. **Goal C, another store.** Not possible per variable in Elsa 4 today: other storage drivers apply only to a reusable
   activity's top-level inputs, outputs and graph variables, and only the default JSON storage ships. The owner writes
   their own plan or spec before the workflow goes live, answering: what is stored and how large; who reads it; its
   lifetime and retention; what happens to an external write when the workflow faults, retries or is cancelled; the
   store's credentials (a `Secret` reference) and access rights; the activities that read and write the store
   explicitly; what happens to values Elsa 3 already wrote for running instances; and whether to move to an Elsa 4
   storage driver once one exists. The migration plan links that plan or spec.
5. **Goal D.** The default storage is fine. Record the decision.

Record per driver: the goal, the decision, the Elsa 4 feature chosen or the link to the owner's plan, and the
follow-up (none, a change to the Elsa 3 definition, or a plan to write). The finding repeats on every run; it does not
reopen the row. For further reading, the repository's guide `docs/migration/elsa3-storage-drivers.md` holds the same
conversation in more detail.

## Case 18: Feature registration

**Verdict**: port

**Applies when**: always: every ported component lives in an Elsa 4 feature the migration host loads.

**Search**: `: FeatureBase`, `AddActivitiesFrom<`, `elsa.Use`, `UseXxx(` extension methods on the Elsa 3 module

**Before (Elsa 3)**
```csharp
public class AcmeFeature(IModule module) : FeatureBase(module)
{
    public override void Configure() => Module.AddActivitiesFrom<AcmeFeature>();
    public override void Apply() => Services.AddScoped<IInvoiceClient, InvoiceClient>();
}

services.AddElsa(elsa => elsa.Use<AcmeFeature>());
```

**After (Elsa 4)** <!-- sample: case-18 -->
```csharp
// The user's own Elsa 4 feature. The migration host references this project and lists the feature in its
// local settings; the activities are found in the feature's assembly, so they need no registration here.
[ShellFeature("Acme", DisplayName = "Acme", Description = "Acme activities, script functions and variable types.")]
public sealed class AcmeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Services the activities take through their constructors (case 10).
        services.TryAddSingleton<IInvoiceClient, InMemoryInvoiceClient>();

        // The trigger's stimulus provider (case 9).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, InvoiceReceivedStimulusProvider>());

        // JavaScript host functions (case 14).
        services.AddSingleton(AcmeScriptFunctions.LookupCustomer);

        // Variable types (case 16).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IVariableTypeDescriptorProvider, AcmeVariableTypes>());
    }
}
```

**Steps**:
1. Create a class library for the feature in your own repository, referencing the Elsa 4 projects your components use
   (the activity authoring contracts, and the JavaScript or variable-type contracts when you need them).
2. Write one `IShellFeature` marked `[ShellFeature("<name>")]`. Register services, stimulus providers, host functions
   and variable type providers in `ConfigureServices`. Activities need no registration: they are found in the
   feature's assembly.
3. Group components into features as the plan records. One feature per Elsa 3 feature is a good default.
4. The migration host references the project and lists the feature in its local settings (see SKILL.md).

## Case 19: Tests

**Verdict**: port

**Applies when**: the component has tests in Elsa 3.

**Search**: `ActivityTestFixture`, `RunActivityAsync`, `Elsa.Testing.Shared`, test projects that reference the
component

**Before (Elsa 3)**
```csharp
[Fact]
public async Task Sends_the_invoice()
{
    var fixture = new ActivityTestFixture(new SendInvoice { CustomerId = new("c-1"), Amount = new(125m) })
        .ConfigureServices(services => services.AddSingleton<IInvoiceClient>(new FakeInvoiceClient()));

    var context = await fixture.ExecuteAsync();

    Assert.Equal("INV-0001", context.GetActivityOutput(() => fixture.Activity.Result));
}
```

**After (Elsa 4)** <!-- sample: case-19 -->
```csharp
[Fact]
public async Task Sends_the_invoice_and_completes_with_its_number()
{
    var invoices = new FakeInvoiceClient();
    IActivity activity = new SendInvoice(invoices) { CustomerId = "c-1", Amount = 125m };
    var context = new ActivityExecutionContext("workflow-1", "invocation-1", "attempt-1", "node-1", CancellationToken.None);

    var transition = await activity.ExecuteAsync(context);

    var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendInvoiceResult>>(transition);
    Assert.Equal(new SendInvoiceResult("INV-0001", "EUR"), completion.Result);
    Assert.Equal(("c-1", 125m, "EUR"), Assert.Single(invoices.Sent));
}
```

**Steps**:
1. Write xunit tests that create the activity with fakes for its constructor services, build an
   `ActivityExecutionContext`, call the activity through `IActivity.ExecuteAsync`, and assert on the transition.
2. For a stateful activity, call it through `IStatefulActivity<TResult, TState, TTrigger>`: `ExecuteAsync` for the
   suspension, then `ResumeAsync` on a fresh instance with an `ActivityResumeContext`.
3. For variables and workflow state, pass fakes with `context.WithVariables(...)` and `context.WithWorkflowState(...)`.
4. Record the ported tests in the plan's component row.
