using Kernel.Runtime.Conformance;
using Kernel.Runtime.Orleans.Conformance;
using InMemoryProvider = Kernel.Store.Conformance.InMemoryProvider;

// The runtime conformance suite against the Orleans adapter, on Orleans' in-process test cluster and the in-memory store:
//   dotnet runtime/Kernel.Runtime.Orleans.Conformance/bin/Release/net10.0/Kernel.Runtime.Orleans.Conformance.dll semantic
//   dotnet ... semantic-broken
//   dotnet ... stress 20 2000
//   dotnet ... stress-broken 10 1000
return await Cli.RunAsync(new OrleansProvider(), new InMemoryProvider(), args);
