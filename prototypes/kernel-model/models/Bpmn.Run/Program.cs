using Kernel.Bpmn.Run;

// Usage:
//   dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll smoke
//   dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll cost sqlite
//   dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll cost postgres "<connection string>"

if (args is ["smoke"]) return await Smoke.RunAsync() + await CaseSmoke.RunAsync();
if (args is ["cost", "sqlite"])
{
    var file = Path.Combine(Path.GetTempPath(), $"kernel-bpmn-{Guid.NewGuid():N}.db");
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(false, $"Data Source={file}", inBatch);
    File.Delete(file);
    return failures;
}
if (args is ["cost", "postgres", var connection])
{
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(true, connection, inBatch);
    return failures;
}
Console.Error.WriteLine("smoke | cost sqlite | cost postgres <connection>");
return 2;
