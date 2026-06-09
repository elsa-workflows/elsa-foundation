using Elsa.Expressions.Core.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Abstractions
{
    /// <summary>
    /// Base class for custom activities with auto-complete behavior.
    /// </summary>
    public abstract class CodeActivity : ActivityBase
    {
        /// <inheritdoc />
        protected CodeActivity() : base()
        {
        }

        /// <inheritdoc />
        protected CodeActivity(string activityType, string version = "1.0.0" /*string? source = default, int? line = default*/)
            : base(activityType, version /*source, line*/)
        {
        }
    }

    /// <summary>
    /// Base class for custom activities with auto-complete behavior that return a result.
    /// </summary>
    public abstract class CodeActivityWithResult : CodeActivity
    {
        /// <inheritdoc />
        protected CodeActivityWithResult(/*string? source = default, int? line = default*/) : base(/*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivityWithResult(string activityType, string version = "1.0.0" /*string? source = default, int? line = default*/)
            : base(activityType, version /*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivityWithResult(IMemoryBlockReference? output /*string? source = default, int? line = default*/)
            : base()
        {
            if (output is not null)
            {
                Result = new OutputArgument(output);
            }
        }

        /// <inheritdoc />
        protected CodeActivityWithResult(OutputArgument? output /*string? source = default, int? line = default*/)
            : base()
        {
            Result = output;
        }

        /// <summary>
        /// The result of the activity.
        /// </summary>
        public OutputArgument? Result { get; set; }
    }

    /// <summary>
    /// Base class for custom activities with auto-complete behavior that return a result.
    /// </summary>
    public abstract class CodeActivity<T> : CodeActivity, IActivityWithResult<T>
    {
        /// <inheritdoc />
        protected CodeActivity(/*string? source = default, int? line = default*/) : base(/*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivity(string activityType, string version = "1.0.0" /*string? source = default, int? line = default*/ )
            : base(activityType, version /*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivity(IMemoryBlockReference? output /*string? source = default, int? line = default*/)
            : base()
        {
            if (output is not null)
            {
                Result = new OutputArgument<T>(output);
            }
        }

        /// <inheritdoc />
        protected CodeActivity(OutputArgument<T>? output /*string? source = default, int? line = default*/)
            : base()
        {
            Result = output;
        }

        /// <summary>
        /// The result of the activity.
        /// </summary>
        public OutputArgument<T>? Result { get; set; }

        OutputArgument? IActivityWithResult.Result
        {
            get => Result;
            set => Result = (OutputArgument<T>?)value;
        }
    }
}
