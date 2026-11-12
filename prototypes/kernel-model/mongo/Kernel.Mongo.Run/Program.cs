using Kernel.Mongo;
using Kernel.Mongo.Run;
using Kernel.Store.Conformance;

// Usage (MongoDB 8.0 as a replica set; directConnection for a single node whose member name is the container's):
//   dotnet mongo/Kernel.Mongo.Run/bin/Release/net10.0/Kernel.Mongo.Run.dll "mongodb://localhost:<port>/?directConnection=true" semantic
//   ... <connection> stress 40 5000            the conformance stress test
//   ... <connection> stress-broken 15 1500     the same with the fence broken on purpose: it must fail
//   ... <connection> cost                      round trips of the reference runs
//   ... <connection> --fence-in-batch cost     the same with the fence in the bulk write
//   ... <connection> probe ...                 see Probes.cs

if (args.Length < 2)
{
    Console.Error.WriteLine("<connection> [--fence-in-batch] semantic|semantic-broken|stress|stress-broken|cost|probe ...");
    return 2;
}
var connection = args[0];
var rest = args.Skip(1).ToList();
var options = new MongoStoreOptions { FenceInBatch = rest.Remove("--fence-in-batch") };
// Switch off a remedy to see what it fixes (MONGO.md, M2 and M3).
if (rest.Remove("--no-abort")) MongoStore.AbortOnFailure = false;
if (rest.Remove("--no-backoff")) MongoStore.BackOffOnTransient = false;
if (rest.Remove("--labels-only")) MongoStore.LabelsOnly = true;
if (rest is ["probe", .. var probe]) return await Probes.RunAsync(connection, options, probe.ToArray());
using var provider = new MongoProvider(connection, options);
return await Cli.RunAsync(provider, rest.ToArray());
