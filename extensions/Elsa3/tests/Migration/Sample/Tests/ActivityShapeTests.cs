using System.Reflection;
using Acme;
using Acme.Legacy;
using Elsa.Activities.Runtime.Core.Attributes;
using Xunit;

namespace Elsa3.Migration.Sample.Tests;

/// <summary>
/// Cases 02, 04 and 06: the sample keeps the authored shape the catalog teaches (full name, replaced-type marker,
/// output keys, declared outcomes), so an edit that drops one fails here. How the CLR scanner and the converter read
/// that shape is covered by their own tests.
/// </summary>
public sealed class ActivityShapeTests
{
    [Fact]
    public void Activity_full_name_is_the_elsa3_type_name()
    {
        Assert.Equal("Acme.SendInvoice", typeof(SendInvoice).FullName);
        Assert.Empty(typeof(SendInvoice).GetCustomAttributes<ReplacesActivityTypeAttribute>());
    }

    [Fact]
    public void Activity_whose_elsa3_name_cannot_be_a_class_names_the_type_it_replaces()
    {
        var marker = Assert.Single(typeof(Notify).GetCustomAttributes<ReplacesActivityTypeAttribute>());

        Assert.Equal("Acme.Legacy.Notify-v2", marker.TypeName);
    }

    [Theory]
    [InlineData(typeof(SendInvoiceResult), nameof(SendInvoiceResult.InvoiceNumber))]
    [InlineData(typeof(InvoiceStatusResult), nameof(InvoiceStatusResult.Status))]
    [InlineData(typeof(PaymentResult), nameof(PaymentResult.AmountPaid))]
    [InlineData(typeof(InvoiceReceivedResult), nameof(InvoiceReceivedResult.InvoiceNumber))]
    [InlineData(typeof(CustomerNoteResult), nameof(CustomerNoteResult.Note))]
    [InlineData(typeof(ChargeResult), nameof(ChargeResult.Charged))]
    public void Elsa3_result_keeps_the_output_key_result(Type resultType, string property)
    {
        Assert.Equal("Result", resultType.GetProperty(property)!.GetCustomAttribute<OutputAttribute>()!.Key);
    }

    [Fact]
    public void Other_elsa3_outputs_keep_their_names()
    {
        var currency = typeof(SendInvoiceResult).GetProperty(nameof(SendInvoiceResult.Currency))!;

        Assert.Equal("InvoiceCurrency", currency.GetCustomAttribute<OutputAttribute>()!.Key);
    }

    [Fact]
    public void Outcomes_are_declared_with_their_elsa3_names()
    {
        var outcomes = typeof(Approve).GetCustomAttributes<ActivityOutcomeAttribute>().Select(outcome => outcome.Key);

        Assert.Equal(["Approved", "Rejected"], outcomes.Order());
    }
}
