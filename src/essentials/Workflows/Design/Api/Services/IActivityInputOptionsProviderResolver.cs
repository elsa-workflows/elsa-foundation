using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

public interface IActivityInputOptionsProviderResolver
{
    IActivityInputOptionsProvider? Find(string key);
}
