namespace Elsa.Activities.Design.Core.Models
{
    public sealed record ActivityDisplayInfo(
        double X,
        double Y,
        double Width,
        double Height
    )
    {
        public ActivityDisplayInfo() : this(0, 0, 0, 0)
        {
        }
    }
}
