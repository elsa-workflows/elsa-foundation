using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Expressions.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Handlers;

public sealed class ListVariableTypeDescriptorsRequestHandler(IVariableTypeDescriptorCatalog catalog)
    : IRequestHandler<ListVariableTypeDescriptors, VariableTypeDescriptorsResponse>
{
    public Task<VariableTypeDescriptorsResponse> Handle(ListVariableTypeDescriptors request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptors = catalog.GetDescriptors()
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Alias, StringComparer.Ordinal)
            .Select(descriptor => new VariableTypeDescriptorView(
                descriptor.Alias,
                descriptor.DisplayName,
                descriptor.Category,
                descriptor.DefaultEditor,
                descriptor.SupportedCollectionKinds.Order().ToArray(),
                descriptor.SupportsNull,
                descriptor.SupportsDurability,
                descriptor.CompatibleStorageDriverKeys.Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        return Task.FromResult(new VariableTypeDescriptorsResponse(descriptors));
    }
}
