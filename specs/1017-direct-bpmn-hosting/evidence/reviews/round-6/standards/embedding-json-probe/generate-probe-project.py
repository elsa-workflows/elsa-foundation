#!/usr/bin/env python3
"""Generate the isolated standards probe project from explicit source-built Workbench products."""
import argparse
from pathlib import Path
from xml.sax.saxutils import escape
parser=argparse.ArgumentParser();parser.add_argument('--products',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);a=parser.parse_args()
products=a.products.resolve(strict=True);output=a.output.resolve();output.mkdir(parents=True,exist_ok=False)
for needed in ['Elsa.Activities.Bpmn.dll','Elsa.Workflows.Runtime.Core.dll','Elsa.Workflows.Runtime.dll']:assert (products/needed).exists(),needed
refs=''.join('<Reference Include="'+escape(f.stem)+'"><HintPath>'+escape(str(f))+'</HintPath></Reference>' for f in sorted(products.glob('*.dll')))
(output/'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/>'+refs+'</ItemGroup></Project>')
(output/'Program.cs').write_bytes(Path(__file__).with_name('Program.cs').read_bytes())
print(output/'Probe.csproj')
