using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

/// <summary>The additive session table leaves existing protected flow/preview payloads unchanged.</summary>
[EfSchemaUpcaster("1.0.0", "1.1.0")]
internal sealed class AuthenticationStateV1Upcaster : IEfSchemaUpcaster
{
    public AuthenticationStateV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
