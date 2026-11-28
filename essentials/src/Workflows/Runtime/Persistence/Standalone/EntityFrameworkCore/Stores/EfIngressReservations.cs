using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    partial void GetReservationAdmissionFence(RuntimeIngressAdmissionRequest request, ref Func<CancellationToken, ValueTask>? fence)
    {
        var expected = request.Reservation ?? throw new InvalidDataException("A routed source has no reservation.");
        fence = async token =>
        {
            RuntimeIngressReservationRules.CheckAdmission(request, expected);
            await new EfIngressReservationStore(context, access).FenceAsync(expected, token);
        };
    }
}
