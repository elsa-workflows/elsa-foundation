# Isolated checkpoint JSON boundary probe

This focused in-process probe uses existing source-built Workbench products. It constructs inert private-state inputs/readers and calls the actual BPMN checkpoint enricher and public checkpoint committer. It performs no database, queue, receipt or authority writes and launches no product host. It does not establish actual-host recovery behavior.

The retained run used source9c products listed in `provenance.json`; the relevant source trees are identical to reviewed d144. `output.log` contains three raw JSON exception observations and the retained-predecessor/zero-persistence counts. The process exits normally because it records expected observations; this is not a passing product behavior assertion or red/green fix proof.

To reproduce, build source d144 or the recorded source9c in a separate checkout/output boundary, honoring the machine build-slot wrapper. Do not replace a running host's products. Generate a fresh private project with:

```sh
python3 generate-probe-project.py --products /absolute/path/to/explicit/workbench/products --output /fresh/private/probe
```

Then run `dotnet run --project /fresh/private/probe/Probe.csproj --configuration Release`. The project references only the explicit products and the installed .NET/ASP.NET 10 runtime. All project intermediates and outputs stay under the new probe directory. The generator and `Program.cs` are the portable inputs; compiled probe DLLs and generated machine-specific project paths are not retained or required.
