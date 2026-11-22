namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity type supports execution as a durable background invocation.
/// </summary>
/// <remarks>
/// This is an eligibility declaration, not a scheduling implementation. The activity must still
/// satisfy the runtime's background execution contract and return a durable typed result.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class BackgroundExecutionSafeAttribute : Attribute;
