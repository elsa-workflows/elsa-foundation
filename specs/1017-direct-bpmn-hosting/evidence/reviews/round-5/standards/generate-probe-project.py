from pathlib import Path
import argparse, platform, shutil, xml.sax.saxutils as xml
parser=argparse.ArgumentParser(description="Generate the round 5 standards probes against explicit source-built Workbench products.")
parser.add_argument("--products",type=Path,required=True);parser.add_argument("--output",type=Path,required=True);args=parser.parse_args()
products=args.products.resolve(strict=True);output=args.output.resolve();output.mkdir(parents=True,exist_ok=False)
refs="".join(f'<Reference Include="{xml.escape(p.stem)}"><HintPath>{xml.escape(str(p))}</HintPath></Reference>'for p in sorted(products.glob("*.dll")))
assert (products/"Elsa.Workflows.Runtime.dll").exists() and (products/"Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll").exists()
system=platform.system();machine=platform.machine().lower();arch="arm64"if machine in("arm64","aarch64")else"x64"
rid=("osx-"if system=="Darwin"else"win-"if system=="Windows"else"linux-")+arch
native="".join(f'<Content Include="{xml.escape(str(p))}" Link="{xml.escape(p.name)}" CopyToOutputDirectory="PreserveNewest"/>'for p in(products/"runtimes"/rid/"native").glob("*"))
(output/"Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/>'+refs+native+'</ItemGroup></Project>')
for name in("Program.cs","ProviderProbe.cs"):shutil.copyfile(Path(__file__).parent/"runtime-json-probe"/name,output/name)
print(output/"Probe.csproj")
