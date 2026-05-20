using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Activities.Runtime.Core
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
        protected CodeActivity(string activityType, int version = 1 /*string? source = default, int? line = default*/)
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
        protected CodeActivityWithResult(string activityType, int version = 1 /*string? source = default, int? line = default*/)
            : base(activityType, version /*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivityWithResult(IMemoryBlockReference? output /*string? source = default, int? line = default*/)
            : base()
        {
            if (output is not null)
            {
                Result = new ActivityOutput(output);
            }
        }

        /// <inheritdoc />
        protected CodeActivityWithResult(ActivityOutput? output /*string? source = default, int? line = default*/)
            : base()
        {
            Result = output;
        }

        /// <summary>
        /// The result of the activity.
        /// </summary>
        public ActivityOutput? Result { get; set; }
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
        protected CodeActivity(string activityType, int version = 1 /*string? source = default, int? line = default*/ )
            : base(activityType, version /*source, line*/)
        {
        }

        /// <inheritdoc />
        protected CodeActivity(IMemoryBlockReference? output /*string? source = default, int? line = default*/)
            : base()
        {
            if (output is not null)
            {
                Result = new ActivityOutput<T>(output);
            }
        }

        /// <inheritdoc />
        protected CodeActivity(ActivityOutput<T>? output /*string? source = default, int? line = default*/)
            : base()
        {
            Result = output;
        }

        /// <summary>
        /// The result of the activity.
        /// </summary>
        public ActivityOutput<T>? Result { get; set; }

        ActivityOutput? IActivityWithResult.Result
        {
            get => Result;
            set => Result = (ActivityOutput<T>?)value;
        }
    }
}
