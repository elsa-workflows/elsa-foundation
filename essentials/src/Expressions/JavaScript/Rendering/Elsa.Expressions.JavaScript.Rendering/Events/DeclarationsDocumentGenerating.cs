using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Events;

internal sealed record DeclarationsDocumentGenerating(IJavaScriptDeclarationsContributionContext Context) : IEvent;