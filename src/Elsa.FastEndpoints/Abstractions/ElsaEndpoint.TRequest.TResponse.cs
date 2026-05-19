using Elsa.FastEndpoints.Constants;
using FastEndpoints;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.FastEndpoints.Abstractions
{
    public class ElsaEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse> where TRequest : notnull, new() where TResponse : notnull
    {
        protected void ConfigurePermissions(params string[] permissions)
        {
            if (!EndpointSecurityOptions.SecurityIsEnabled)
                AllowAnonymous();
            else
                Permissions([PermissionNames.All, .. permissions]);
        }
    }
}
