using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Availability;

internal sealed class SaveSettings(ICommandSender commandSender, ILogger<SaveSettings> logger) : ElsaCommandHandlerEndpoint<SaveActivityAvailabilitySettings, ActivityAvailabilitySettings>(commandSender, logger)
{
    public override void Configure()
    {
        Put(RouteConstants.GetRoute("availability/settings"));
        ConfigurePermissions();
    }
}
