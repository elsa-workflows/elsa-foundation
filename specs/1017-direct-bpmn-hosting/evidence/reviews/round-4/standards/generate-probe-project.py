from pathlib import Path
import argparse,shutil,xml.sax.saxutils as xml
parser=argparse.ArgumentParser(description='Generate the read-only standards negative-probe project against explicitly built Workbench products.')
parser.add_argument('--products',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();products=args.products.resolve(strict=True);output=args.output.resolve();output.mkdir(parents=True,exist_ok=False)
refs=''.join(f'<Reference Include="{xml.escape(p.stem)}"><HintPath>{xml.escape(str(p))}</HintPath></Reference>'for p in sorted(products.glob('*.dll')))
assert (products/'Elsa.Activities.Bpmn.dll').exists()and refs
(output/'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/>'+refs+'</ItemGroup></Project>')
shutil.copyfile(Path(__file__).parent/'probe/Program.cs',output/'Program.cs')
print(output/'Probe.csproj')
