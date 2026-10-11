using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Events;

public sealed record DeclarationsDocumentGenerating(IJavaScriptDeclarationsContributionContext Context) : IEvent;