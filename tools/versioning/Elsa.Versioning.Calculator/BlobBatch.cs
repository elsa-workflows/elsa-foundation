using System.Diagnostics;
using System.Text;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// Reads blobs by object id through one <c>git cat-file --batch</c> process: each request is the id on a line, each
/// answer a <c>&lt;id&gt; &lt;type&gt; &lt;size&gt;</c> header, the bytes and a newline, or <c>&lt;id&gt; missing</c>.
/// </summary>
internal sealed class BlobBatch : IDisposable
{
    private readonly Process process;
    private readonly Stream input;
    private readonly Stream output;

    public BlobBatch(string directory)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in (string[])["-C", directory, "cat-file", "--batch"])
            startInfo.ArgumentList.Add(argument);

        process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git cat-file --batch.");
        // Drained so a warning on standard error can never block the process.
        _ = process.StandardError.ReadToEndAsync();
        input = process.StandardInput.BaseStream;
        output = process.StandardOutput.BaseStream;
    }

    /// <summary>The blob's bytes; fails when the repository has no blob with that id.</summary>
    public byte[] Read(string objectId)
    {
        input.Write(Encoding.ASCII.GetBytes(objectId + "\n"));
        input.Flush();

        var header = ReadLine();
        var fields = header.Split(' ');
        if (fields.Length != 3 || fields[1] != "blob" || !int.TryParse(fields[2], out var size))
            throw new InvalidOperationException($"{objectId} names no file in the repository (git cat-file --batch: '{header}').");

        var content = new byte[size];
        output.ReadExactly(content);
        if (output.ReadByte() != '\n')
            throw new InvalidOperationException($"git cat-file --batch did not end {objectId} with a newline.");

        return content;
    }

    public void Dispose()
    {
        input.Dispose();
        if (!process.WaitForExit(5000))
            process.Kill();
        process.Dispose();
    }

    private string ReadLine()
    {
        var line = new List<byte>();
        for (var next = output.ReadByte(); next != '\n'; next = output.ReadByte())
        {
            if (next < 0)
                throw new InvalidOperationException("git cat-file --batch exited before answering.");
            line.Add((byte)next);
        }

        return Encoding.ASCII.GetString(line.ToArray());
    }
}
