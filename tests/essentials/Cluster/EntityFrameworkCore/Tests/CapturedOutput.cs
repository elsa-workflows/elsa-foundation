using System.Text;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>The lines a child process wrote, collected from its output events and read from any thread for assertion messages.</summary>
internal sealed class CapturedOutput
{
    private readonly StringBuilder _lines = new();

    public void Append(string? line)
    {
        if (line is null)
            return;

        lock (_lines)
            _lines.AppendLine(line);
    }

    public override string ToString()
    {
        lock (_lines)
            return _lines.ToString();
    }
}
