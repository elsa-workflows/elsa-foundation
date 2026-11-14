using Kernel.Bpmn.Run;

// Usage:
//   dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll smoke

if (args is ["smoke"]) return await Smoke.RunAsync();
Console.Error.WriteLine("smoke");
return 2;
