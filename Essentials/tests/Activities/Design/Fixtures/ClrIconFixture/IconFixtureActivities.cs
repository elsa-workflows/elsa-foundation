using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.ClrIconFixture;

public abstract class IconFixtureActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

/// <summary>Declares a lucide designer icon that the scanner must project onto an icon facet.</summary>
[Activity(Icon = "Rocket")]
public sealed class LucideIconFixtureActivity : IconFixtureActivity;

/// <summary>Declares a kebab lucide name; the scanner must persist it verbatim.</summary>
[Activity(Icon = "git-branch")]
public sealed class KebabLucideIconFixtureActivity : IconFixtureActivity;

/// <summary>Declares a static-web-asset icon in the extension package.</summary>
[Activity(Icon = "asset:Acme.Slack/icons/slack.svg")]
public sealed class AssetIconFixtureActivity : IconFixtureActivity;

/// <summary>Declares a client-registered extension icon.</summary>
[Activity(Icon = "ext:slack")]
public sealed class ExtensionIconFixtureActivity : IconFixtureActivity;

/// <summary>An invalid icon must not produce a catalog facet.</summary>
[Activity(Icon = "asset:../secrets/key.svg")]
public sealed class InvalidIconFixtureActivity : IconFixtureActivity;

/// <summary>A base activity whose <c>[Activity(Icon)]</c> a derived type must inherit.</summary>
[Activity(Icon = "Boxes")]
public abstract class IconBaseActivity : IconFixtureActivity;

/// <summary>Inherits <c>Boxes</c> from <see cref="IconBaseActivity"/> and declares no icon of its own.</summary>
public sealed class InheritedIconFixtureActivity : IconBaseActivity;
