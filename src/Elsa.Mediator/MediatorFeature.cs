using CShells.Features;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Mediator;

[ShellFeature(
    name: "Mediator"
)]
public class MediatorFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDomainEventSender, DomainEventSender>();
    }
}
