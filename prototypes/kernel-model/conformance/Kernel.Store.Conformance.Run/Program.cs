using Kernel.Store.Conformance;

// The conformance suite against the reference, the in-memory store:
//   dotnet conformance/Kernel.Store.Conformance.Run/bin/Release/net10.0/Kernel.Store.Conformance.Run.dll semantic
//   dotnet ... stress 40 5000
//   dotnet ... stress-broken 15 1500
return await Cli.RunAsync(new InMemoryProvider(), args);
