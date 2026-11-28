using System.Globalization;
using System.Text;
using Kernel.Native;

namespace Kernel.Testing;

internal static class NativeTerminalFixture
{
    internal const long Envelope = 16_457_728;
    internal const int PublicHeaderReserve = 16_384 + 8_192;

    internal static int Bytes(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);

    internal static long RowBytes(InstanceRow row, int variableHeader) =>
        16_384L + variableHeader
        + Math.Max(Bytes(row.Children), 384L * ChildRegistry.Read(row.Children).Count)
        + Math.Max(Bytes(row.Jobs), 12_416L * JobRegistry.Read(row.Jobs).Count)
        + (row.Cascade?.Entries.StorageBytes ?? 0)
        + Bytes(row.State) + Bytes(row.CustomHandles) + Bytes(row.CommitTimes) + Bytes(row.Model) + Bytes(row.Definition);

    internal sealed class OpenInbox : CompositeActivity
    {
        [Input] public bool Protected { get; set; }
        protected override void Execute(CompositeContext context) =>
            context.RegisterInputInbox(1, 1, Protected ? TimeSpan.FromHours(1) : null);
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("An invocation inbox has no authored children.");
    }

    internal sealed class Padding : CompositeActivity, ISignalledComposite
    {
        protected override void Execute(CompositeContext context) => context.SetState("");
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal) =>
            context.SetState(new string('x', int.Parse(signal.Value!, CultureInfo.InvariantCulture)));
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("Padding has no authored children.");
    }
}
