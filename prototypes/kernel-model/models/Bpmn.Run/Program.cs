using Kernel.Bpmn.Run;

// Usage:
//   dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll smoke

if (args is ["smoke"]) return await Smoke.RunAsync();
if (args is ["identity-list"]) { foreach (var name in CreationChecks.Names) Console.WriteLine(name); return 0; }
if (args is ["identity"]) { await CreationChecks.Run(); return 0; }
Console.Error.WriteLine("smoke");
return 2;
