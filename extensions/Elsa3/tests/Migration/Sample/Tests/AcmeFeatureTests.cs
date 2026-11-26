using System.Text.Json;
using Acme;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Migration.Sample.Tests;

/// <summary>Cases 9, 16 and 18: the feature's registrations, the trigger and the variable type.</summary>
public sealed class AcmeFeatureTests
{
    [Fact]
    public void Feature_registers_its_services()
    {
        var services = new ServiceCollection();
        new AcmeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IInvoiceClient>());
        Assert.Contains(provider.GetServices<IActivityTriggerStimulusProvider>(), item => item is InvoiceReceivedStimulusProvider);
        Assert.Contains(provider.GetServices<JavaScriptHostFunctionDeclaration>(), item => item.Name == "acme.lookupCustomer");
        Assert.Contains(provider.GetServices<IVariableTypeDescriptorProvider>(), item => item is AcmeVariableTypes);
    }

    [Fact]
    public void Variable_type_is_registered_under_a_dotted_alias()
    {
        var descriptor = Assert.Single(new AcmeVariableTypes().GetDescriptors());

        Assert.Equal("acme.Invoice", descriptor.Alias);
        Assert.Contains('.', descriptor.Alias);
        Assert.Equal(typeof(Invoice), descriptor.ClrType);
    }

    [Fact]
    public async Task Trigger_completes_when_an_invoice_starts_the_workflow_here()
    {
        IActivity activity = new InvoiceReceived { Mailbox = "invoices@acme.test" };
        var context = new ActivityExecutionContext("workflow-1", "invocation-1", "attempt-1", "node-1", CancellationToken.None,
            JsonSerializer.SerializeToElement(new { invoiceNumber = "INV-0042" }), triggerNodeId: "node-1");

        var transition = await activity.ExecuteAsync(context);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<InvoiceReceivedResult>>(transition);
        Assert.Equal("INV-0042", completion.Result.InvoiceNumber);
    }

    [Fact]
    public async Task Trigger_waits_for_the_next_invoice_otherwise_and_resumes_on_it()
    {
        IStatefulActivity<InvoiceReceivedResult, InvoiceWaitState, IncomingInvoice> waiting = new InvoiceReceived { Mailbox = "invoices@acme.test" };

        var suspended = await waiting.ExecuteAsync(TestContext.Create());

        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<InvoiceWaitState>>(suspended);
        var registration = Assert.IsType<ActivityTriggerRegistration<IncomingInvoice>>(Assert.Single(suspension.Registrations));
        Assert.Equal(InvoiceReceived.StimulusType, registration.StimulusType);
        Assert.Equal(InvoiceReceived.Hash("invoices@acme.test"), registration.StimulusHash);

        IStatefulActivity<InvoiceReceivedResult, InvoiceWaitState, IncomingInvoice> resumed = new InvoiceReceived();
        var completed = await resumed.ResumeAsync(new ActivityResumeContext<InvoiceWaitState, IncomingInvoice>(
            TestContext.Create(), suspension.State, new IncomingInvoice("INV-0043"), "registration-1", "delivery-1"));

        Assert.Equal("INV-0043", Assert.IsAssignableFrom<IActivityCompletionTransition<InvoiceReceivedResult>>(completed).Result.InvoiceNumber);
    }

    [Fact]
    public void Stimulus_provider_describes_the_mailbox_stimulus()
    {
        var result = new InvoiceReceivedStimulusProvider().Describe(Node(InvoiceReceived.ActivityType, "invoices@acme.test"));

        Assert.True(result.IsRecognized);
        var stimulus = Assert.Single(result.Descriptors);
        Assert.Equal(InvoiceReceived.StimulusType, stimulus.StimulusType);
        Assert.Equal(InvoiceReceived.Hash("invoices@acme.test"), stimulus.StimulusHash);
    }

    [Fact]
    public void Stimulus_provider_ignores_other_activities()
    {
        Assert.False(new InvoiceReceivedStimulusProvider().Describe(Node("Acme.Other", "x")).IsRecognized);
    }

    private static ExecutableNode Node(string activityType, string mailbox)
    {
        var type = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding(
            nameof(InvoiceReceived.Mailbox),
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(mailbox), ValueProtectionPolicy.InstanceInline));
        using var document = JsonDocument.Parse("{}");
        return new ExecutableNode(
            executableNodeId: "node-1",
            authoredActivityId: "authored-node-1",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [nameof(InvoiceReceived.Mailbox)] = binding },
            metadata: new Dictionary<string, string>());
    }
}
