using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Activities.Design.Tests.ClrIconFixture;

/// <summary>Declares replaced type names, one twice and one padded, that the scanner must record trimmed, distinct and sorted.</summary>
[ReplacesActivityType("Acme.SendInvoice")]
[ReplacesActivityType(" Acme.Legacy.SendInvoice ")]
[ReplacesActivityType("Acme.SendInvoice")]
public sealed class ReplacingFixtureActivity : IconFixtureActivity;

/// <summary>A base declaring a replaced type name, which a derived activity does not inherit.</summary>
[ReplacesActivityType("Acme.Base")]
public abstract class ReplacingBaseFixtureActivity : IconFixtureActivity;

/// <summary>Derives from <see cref="ReplacingBaseFixtureActivity"/> and declares no replaced type of its own.</summary>
public sealed class DerivedFromReplacingFixtureActivity : ReplacingBaseFixtureActivity;
