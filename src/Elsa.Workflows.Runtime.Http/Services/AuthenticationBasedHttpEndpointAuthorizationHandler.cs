using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Models;
using Microsoft.AspNetCore.Authorization;

namespace Elsa.Workflows.Runtime.Http.Services
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationBasedHttpEndpointAuthorizationHandler"/> class.
    /// </summary>
    internal sealed class AuthenticationBasedHttpEndpointAuthorizationHandler(IAuthorizationService authorizationService) : IHttpEndpointAuthorizationHandler
    {
        /// <inheritdoc />
        public async ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
        {
            var httpContext = context.HttpContext;
            var user = httpContext.User;
            var identity = user.Identity;

            if (identity == null)
                return false;

            if (identity.IsAuthenticated == false)
                return false;

            if (string.IsNullOrWhiteSpace(context.Policy))
                return identity.IsAuthenticated;

            var protectedResource = new
            {
                context.Workflow
            };

            var authorizationResult = await authorizationService.AuthorizeAsync(user, protectedResource, context.Policy!);

            return authorizationResult.Succeeded;
        }
    }
}
