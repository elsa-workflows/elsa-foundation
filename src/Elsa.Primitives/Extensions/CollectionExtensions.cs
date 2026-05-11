namespace Elsa.Primitives.Extensions
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// Removes all items from a collection that match the specified predicate.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="predicate">The predicate.</param>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        public static void RemoveWhere<T>(this ICollection<T> collection, Func<T, bool> predicate)
        {
            var itemsToRemove = collection.Where(predicate).ToList();
            foreach (var item in itemsToRemove) collection.Remove(item);
        }
    }
}
