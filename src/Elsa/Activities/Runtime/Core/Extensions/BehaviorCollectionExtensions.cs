using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Extensions
{
    public static class BehaviorCollectionExtensions
    {
        public static void Add<T>(this ICollection<IBehavior> behaviors, IActivity owner)
            where T : IBehavior
            => behaviors.Add((T)Activator.CreateInstance(typeof(T), owner)!);
    }
}
