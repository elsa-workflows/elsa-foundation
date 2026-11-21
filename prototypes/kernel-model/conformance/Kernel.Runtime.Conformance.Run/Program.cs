using Kernel.Runtime.Conformance;
using InMemoryProvider = Kernel.Store.Conformance.InMemoryProvider;

// The runtime conformance suite against the reference, the built-in runtime, on the in-memory store:
//   dotnet conformance/Kernel.Runtime.Conformance.Run/bin/Release/net10.0/Kernel.Runtime.Conformance.Run.dll semantic
//   dotnet ... semantic-broken
//   dotnet ... stress 20 2000
//   dotnet ... stress-broken 10 1000
if (args is ["contracts"]) return ContractBoundaryCheck.Run();
if (args.Contains("semantic", StringComparer.Ordinal) && ContractBoundaryCheck.Run() != 0) return 1;
if (args.Contains("semantic", StringComparer.Ordinal) && await LoopYieldCheck.RunAsync() != 0) return 1;
if (args.Contains("semantic", StringComparer.Ordinal) && await FixedHostHeartbeatCheck.RunAsync() != 0) return 1;
if (args.Contains("semantic", StringComparer.Ordinal) && await WatchdogChecks.RunAsync() != 0) return 1;
return await Cli.RunAsync(new BuiltInProvider(), new InMemoryProvider(), args);
