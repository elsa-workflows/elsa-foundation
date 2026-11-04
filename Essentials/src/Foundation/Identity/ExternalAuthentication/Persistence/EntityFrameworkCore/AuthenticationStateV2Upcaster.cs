using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

/// <summary>Retains old contents without inventing a qualified-link binding; old sessions require fresh sign-in.</summary>
[EfSchemaUpcaster("1.1.0", "1.2.0")]
internal sealed class AuthenticationStateV2Upcaster : IEfSchemaUpcaster
{
    public AuthenticationStateV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
