using Kernel.Runtime.Conformance;
using InMemoryProvider = Kernel.Store.Conformance.InMemoryProvider;

// The runtime conformance suite against the reference, the built-in runtime, on the in-memory store:
//   dotnet conformance/Kernel.Runtime.Conformance.Run/bin/Release/net10.0/Kernel.Runtime.Conformance.Run.dll semantic
//   dotnet ... semantic-broken
//   dotnet ... stress 20 2000
//   dotnet ... stress-broken 10 1000
return await Cli.RunAsync(new BuiltInProvider(), new InMemoryProvider(), args);
