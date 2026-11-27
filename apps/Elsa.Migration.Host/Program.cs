// The temporary Elsa 3 migration host (spec 1236). Removed by the cleanup on #5800. Wiring: MigrationHost.cs.
using Elsa.Migration.Host;

var builder = MigrationHost.CreateBuilder(args);
var app = MigrationHost.Build(builder);
app.Run();

/// <summary>The host's entry point.</summary>
public partial class Program;
