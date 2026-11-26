using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Contracts;

public interface IActivityTypeVersionResolver
{
    string Resolve(Type type, Assembly assembly);
}
