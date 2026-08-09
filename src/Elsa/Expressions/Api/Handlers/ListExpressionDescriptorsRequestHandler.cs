using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Handlers;

public sealed class ListExpressionDescriptorsRequestHandler(IExpressionDescriptorRegistry registry)
    : IRequestHandler<ListExpressionDescriptors, ExpressionDescriptorsResponse>
{
    // Sourced from Elsa.Expressions.Core so the build-time contract fragments publish the same intrinsic
    // set this endpoint serves (one-projection rule, spec 149 / RFC #1191). Hardcoding them here made
    // Literal — used on nearly every input — undiscoverable to a contracts-only consumer.
    private static readonly ExpressionDescriptorView[] IntrinsicAuthoringDescriptors =
        IntrinsicExpressionDescriptors.All
            .Select(descriptor => new ExpressionDescriptorView(
                descriptor.TypeName,
                descriptor.DisplayName,
                Description: null,
                ToView(descriptor.EditingMode)))
            .ToArray();

    public Task<ExpressionDescriptorsResponse> Handle(ListExpressionDescriptors request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptors = IntrinsicAuthoringDescriptors
            .Concat(registry.ListAll().Select(descriptor => new ExpressionDescriptorView(
                descriptor.TypeName,
                descriptor.DisplayName,
                Description: ReadDescription(descriptor),
                ToView(descriptor.EditingMode))))
            .DistinctBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new ExpressionDescriptorsResponse(descriptors));
    }

    private static string? ReadDescription(IExpressionDescriptor descriptor) =>
        descriptor.Properties.TryGetValue("Description", out var description) ? description as string : null;

    private static ExpressionEditingModeView ToView(ExpressionEditingMode mode) => mode switch
    {
        ExpressionEditingMode.Literal => ExpressionEditingModeView.Literal,
        ExpressionEditingMode.Text => ExpressionEditingModeView.Text,
        ExpressionEditingMode.Structured => ExpressionEditingModeView.Structured,
        ExpressionEditingMode.Reference => ExpressionEditingModeView.Reference,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported expression editing mode.")
    };
}
