using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Abstractions
{
    /// <summary>
    /// Base class for custom activities.
    /// </summary>
    [DebuggerDisplay("{Type} - {Id}")]
    public abstract class ActivityBase : IActivity, ISignalHandler
    {
        private readonly ICollection<SignalHandlerRegistration> _signalReceivedHandlers = [];

        /// <summary>        
        /// </summary>
        /// <remarks>
        ///     Should we even be concerned with source and line info? They seem details that the activity shuold not care about when executing
        ///     In addition, they are added to the Custom Properties. I feel the Custom Properties are a way to abuse collecting values in a place where they really should not be.
        /// </remarks>
        protected ActivityBase()
        {
            //this.SetSource(source, line);
            Type = GetType().GetSanitizedFullName();

            // Also this version, it really should not always be one... It is a bit of a useless property now. Perhaps when 
            // revisiting having activity definitions and executions separately. The version does not have to exist on IActivity per se.
            Version = 1;

            // Normally here a ScheduledChildCallbackBehavior was always added automatically. But it has implementation details, which we should not 
            // pre-determine in the core library
            // This base class should really just be a helper tool to facilitate easier usage of the IActivity interface
            // I think we should have an Activity Behavior Manager that per type adds certain behaviors to the activity class. It is a public collection after all.            
        }

        /// <inheritdoc />
        protected ActivityBase(string activityType, int version = 1 /*string? source = null, int? line = null,*/) : this()
        {
            Type = activityType;
            Version = version;
        }

        /// <inheritdoc />
        public string Id { get; set; } = null!;

        /// <inheritdoc />
        public string NodeId { get; set; } = null!;

        /// <inheritdoc />
        public string? Name { get; set; }

        /// <inheritdoc />
        public string Type { get; set; }

        /// <inheritdoc />
        public int Version { get; set; }

        // THESE COMMENTED OUT PROPERTIES ARE USING THE CUSTOM PROPERTIES TO RETRIEVE DATA..
        // PERHAPS REVISE THEM LATER AS NEEDED

        ///// <summary>
        ///// A flag indicating whether this activity can be used for starting a workflow.
        ///// Usually used for triggers, but also used to disambiguate between two or more starting activities and no starting activity was specified.
        ///// </summary>
        //[JsonIgnore]
        //public bool CanStartWorkflow
        //{
        //    get => this.GetCanStartWorkflow();
        //    set => this.SetCanStartWorkflow(value);
        //}

        ///// <summary>
        ///// A flag indicating if this activity should execute synchronously or asynchronously.
        ///// By default, activities with an <see cref="ActivityKind"/> of <see cref="Action"/>, <see cref="Task"/> or <see cref="Trigger"/>
        ///// will execute synchronously, while activities of the <see cref="ActivityKind.Job"/> kind will execute asynchronously.
        ///// </summary>
        //[JsonIgnore]
        //public bool? RunAsynchronously
        //{
        //    get => this.GetRunAsynchronously();
        //    set => this.SetRunAsynchronously(value);
        //}

        //[JsonIgnore]
        //public string? CommitStrategy
        //{
        //    get => this.GetCommitStrategy();
        //    set => this.SetCommitStrategy(value);
        //}

        /// <inheritdoc />
        public Dictionary<string, object> CustomProperties { get; set; } = [];

        /// <inheritdoc />
        [JsonIgnore]
        public Dictionary<string, object> SyntheticProperties { get; set; } = [];

        /// <inheritdoc />
        public Dictionary<string, object> Metadata { get; set; } = [];

        /// <summary>
        /// A collection of reusable behaviors to add to this activity.
        /// </summary>
        [JsonIgnore]
        public ICollection<IBehavior> Behaviors { get; } = [];

        /// <summary>
        /// Override this method to return a value indicating whether the activity can execute.
        /// </summary>
        protected virtual ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context)
        {
            var result = CanExecute(context);
            return new(result);
        }

        /// <summary>
        /// Override this method to return a value indicating whether the activity can execute.
        /// </summary>
        protected virtual bool CanExecute(IActivityExecutionContext context)
        {
            return true;
        }

        /// <summary>
        /// Override this method to implement activity-specific logic.
        /// </summary>
        protected virtual ValueTask ExecuteAsync(IActivityExecutionContext context)
        {
            Execute(context);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Override this method to implement activity-specific logic.
        /// </summary>
        protected virtual void Execute(IActivityExecutionContext context)
        {
        }

        /// <summary>
        /// Override this method to handle any signals sent from downstream activities.
        /// </summary>
        protected virtual ValueTask OnSignalReceivedAsync(object signal, SignalContext context)
        {
            OnSignalReceived(signal, context);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Override this method to handle any signals sent from downstream activities.
        /// </summary>
        protected virtual void OnSignalReceived(object signal, SignalContext context)
        {
        }

        /// <summary>
        /// Register a signal handler delegate.
        /// </summary>
        protected void OnSignalReceived(Type signalType, Func<object, SignalContext, ValueTask> handler) => _signalReceivedHandlers.Add(new SignalHandlerRegistration(signalType, handler));

        /// <summary>
        /// Register a signal handler delegate.
        /// </summary>
        protected void OnSignalReceived<T>(Func<T, SignalContext, ValueTask> handler) => OnSignalReceived(typeof(T), (signal, context) => handler((T)signal, context));

        /// <summary>
        /// Register a signal handler delegate.
        /// </summary>
        protected void OnSignalReceived<T>(Action<T, SignalContext> handler)
        {
            OnSignalReceived<T>((signal, context) =>
            {
                handler(signal, context);
                return ValueTask.CompletedTask;
            });
        }

        /// <summary>
        /// Notify the workflow that this activity completed.
        /// </summary>
        protected static ValueTask CompleteAsync(IActivityExecutionContext context)
        {
            var completionHandler = context.GetRequiredService<IActivityCompletionHandler>();
            return completionHandler.CompleteActivityAsync(context);
        }

        async ValueTask<bool> IActivity.CanExecuteAsync(IActivityExecutionContext context)
        {
            return await CanExecuteAsync(context);
        }

        async ValueTask IActivity.ExecuteAsync(IActivityExecutionContext context)
        {
            await ExecuteAsync(context);

            // Invoke behaviors.
            foreach (var behavior in Behaviors)
                await behavior.ExecuteAsync(context);
        }

        async ValueTask ISignalHandler.ReceiveSignalAsync(object signal, SignalContext context)
        {
            // Give derived activity a chance to do something with the signal.
            await OnSignalReceivedAsync(signal, context);

            // Invoke registered signal delegates for this particular type of signal.
            var signalType = signal.GetType();
            var handlers = _signalReceivedHandlers.Where(x => x.SignalType == signalType);

            foreach (var registration in handlers)
                await registration.Handler(signal, context);

            // Invoke behaviors.
            foreach (var behavior in Behaviors)
                await behavior.ReceiveSignalAsync(signal, context);
        }
    }
}
