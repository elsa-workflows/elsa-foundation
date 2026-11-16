#!/usr/bin/env python3
"""Compile a consumer outside the workspace against public task contracts and the actual Studio package.

Build Runtime Core and Studio Web, pack Elsa.Studio.UserTasks, then supply their binary/archive paths.
This does not deploy, publish packages or contact a running host. Durable/native delivery and the matching
Studio renderer are qualified by UserTaskRuntimeJourneyTests and taskUi.test.tsx respectively.
"""
import argparse
import json
from pathlib import Path
import subprocess
import zipfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--runtime-core', type=Path, required=True)
parser.add_argument('--studio-bin', type=Path, required=True)
parser.add_argument('--studio-package', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True, help='New owned directory outside the repository')
args = parser.parse_args()
output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
with zipfile.ZipFile(args.studio_package) as archive:
    names = archive.namelist()
    for suffix in ['/module.js', '/module.css', '/entry-styles.json']:
        matches = [name for name in names if name.startswith('staticwebassets/studio/modules/user-tasks/') and name.endswith(suffix)]
        if len(matches) != 1 or not archive.read(matches[0]): raise ValueError('Missing or ambiguous packed module asset: ' + suffix)
    assembly = 'lib/net10.0/Elsa.Studio.UserTasks.dll'
    (output / 'Elsa.Studio.UserTasks.dll').write_bytes(archive.read(assembly))
references = [args.runtime_core.resolve(), output / 'Elsa.Studio.UserTasks.dll', args.studio_bin.resolve() / 'CShells.Abstractions.dll', args.studio_bin.resolve() / 'Elsa.Studio.Core.dll']
for path in references:
    if not path.is_file(): raise ValueError('Missing binary: ' + str(path))
project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />'
project += ''.join('<Reference Include="' + escape(path.stem) + '"><HintPath>' + escape(str(path)) + '</HintPath></Reference>' for path in references)
project += '</ItemGroup></Project>'
(output / 'Integration.csproj').write_text(project)
(output / 'Program.cs').write_text(r'''
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Studio.UserTasks;
using Microsoft.Extensions.DependencyInjection;
var provider = new AcmeForms();
var form = new UserTaskForm("external-purchase", "immutable-1", "External purchase", provider.Provider, []);
provider.ValidateDefinition(form);
var empty = JsonSerializer.SerializeToElement(new { });
if ((await provider.ValidateResponseAsync(form, empty, empty, true)).Count != 1) throw new Exception("Invalid external response accepted.");
var data = JsonDocument.Parse("{\"Approval\":{\"AmountUSD\":42,\"CaseSensitiveKey\":\"kept\"}}").RootElement;
if ((await provider.ValidateResponseAsync(form, empty, data, true)).Count != 0) throw new Exception("Valid external response refused.");
var response = new UserTaskResponse("task", "completion", "Approve", data.Clone());
var roundtrip = JsonSerializer.Deserialize<UserTaskResponse>(JsonSerializer.Serialize(response))!;
if (!JsonElement.DeepEquals(data, roundtrip.Data)) throw new Exception("External response changed.");
new UserTasksStudioFeature().ConfigureServices(new ServiceCollection());
Console.WriteLine("PASS: external public-contract provider validation/response roundtrip and packed Studio feature load.");
sealed class AcmeForms : IUserTaskFormProvider
{
    public string Provider => "acme.forms/1";
    public void ValidateDefinition(UserTaskForm form)
    {
        if (form.Id != "external-purchase" || form.Version != "immutable-1") throw new UserTaskException("validation", "Unknown immutable form.");
    }
    public async ValueTask<IReadOnlyDictionary<string, string>> ValidateResponseAsync(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields, CancellationToken cancellationToken = default)
    {
        await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
        return !requireFields || response.TryGetProperty("Approval", out var approval) && approval.TryGetProperty("AmountUSD", out var amount) && amount.TryGetDecimal(out var number) && number > 0
            ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Approval.AmountUSD"] = "Enter a positive external amount." };
    }
}
''')
subprocess.run(['dotnet', 'run', '--project', str(output / 'Integration.csproj')], cwd=output, check=True)
(output / 'qualification.json').write_text(json.dumps({'publicContractConsumer': 'passed', 'packedStudioFeature': 'passed', 'projectReferences': [], 'runtimeCore': str(args.runtime_core.resolve()), 'studioPackage': str(args.studio_package.resolve())}, indent=2))
