using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Contracts
{
    public interface IHttpEndpointRoutesProvider
    {
        Task<IEnumerable<HttpRouteData>> GetRoutes(string path);
    }
}
