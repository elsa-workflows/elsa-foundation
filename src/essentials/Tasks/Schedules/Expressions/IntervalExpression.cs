namespace Elsa.Tasks.Schedules.Expressions;

public sealed class IntervalExpression
{
    public IntervalExpression(IntervalExpressionType intervalExpressionType, string expression)
    {
        Type = intervalExpressionType;
        Expression = expression;
    }

    public static IntervalExpression FromInterval(TimeSpan interval)
    {
        return new IntervalExpression(IntervalExpressionType.Interval, interval.ToString());
    }

    public static IntervalExpression FromCron(string cronExpression)
    {
        return new IntervalExpression(IntervalExpressionType.Cron, cronExpression);
    }

    public IntervalExpressionType Type { get; }
    public string Expression { get; }
}