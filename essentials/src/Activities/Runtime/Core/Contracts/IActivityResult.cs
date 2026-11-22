namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Identifies the single atomic result type returned by an activity.
/// </summary>
/// <typeparam name="TResult">The activity result document type.</typeparam>
/// <remarks>
/// This marker is consumed when a published executable pins an activity contract. It carries no
/// runtime value and is not an output slot or address.
/// </remarks>
public interface IActivityResult<TResult>
{
}
