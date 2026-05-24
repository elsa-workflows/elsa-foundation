using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Mediator.Core.Contracts;

public interface IDomainEventContext
{
    IServiceProvider ServiceProvider { get; }

    IDomainEvent Event { get; }

    CancellationToken CancellationToken { get; }
}
