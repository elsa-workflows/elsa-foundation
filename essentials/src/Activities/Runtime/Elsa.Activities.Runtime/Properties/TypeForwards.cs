using System.Runtime.CompilerServices;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;

[assembly: TypeForwardedTo(typeof(RegisterActivityTypesStartupTask))]
[assembly: TypeForwardedTo(typeof(RuntimeSecretMaskComposition))]
[assembly: TypeForwardedTo(typeof(RuntimeSecretMaskCompositionValidator))]
[assembly: TypeForwardedTo(typeof(RuntimeSecretResolverComposition))]
[assembly: TypeForwardedTo(typeof(RuntimeSecretResolverCompositionValidator))]
