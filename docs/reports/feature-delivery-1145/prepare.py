#!/usr/bin/env python3
"""Prepare an owned, cold-cache FR-2 acceptance deployment. No existing host is used."""
import argparse
import json
import pathlib
import secrets
import string
import subprocess
import urllib.request

IMAGE = "elsaworkflows/elsa-foundation-host@sha256:f418270b598253020291c14eed175807440cc46049b5f945d5df3ddd668a8d43"
FEED = "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json"
ROOTS = [
    "Elsa.Primitives.Hosting", "Elsa.Events", "Elsa.Serialization.SystemText", "Elsa.Tasks",
    "Elsa.Mediator", "Elsa.Expressions", "Elsa.Caching.Memory", "Elsa.Locking.FileSystem",
    "Elsa.Api.Capabilities", "Elsa.Activities.Runtime", "Elsa.Activities.Design.Api",
    "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Design.Api", "Elsa.Workflows.Design.Validations",
    "Elsa.Workflows.Design.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Design.Reconciliation.Json", "Elsa.Workflows.Publishing",
    "Elsa.Workflows.Publishing.Api", "Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Runtime.Api", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore", "Elsa.Workflows.Runtime.Resumption", "Elsa.Workflows.Runtime.Scheduling",
    "Elsa.Foundation.Identity", "Elsa.Foundation.Identity.Api",
    "Elsa.Foundation.Identity.AspNetCoreIdentity",
    "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore",
]


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n" if not isinstance(value, str) else value)


def command(*args):
    return subprocess.check_output(args, text=True).strip()


def prepare(root):
    root.mkdir(parents=True, exist_ok=True)
    for directory in ["restore", "config", "catalog", "packages", "state", "data", "receipts", "fixtures"]:
        (root / directory).mkdir(exist_ok=True)
    if list((root / "state").iterdir()) or list((root / "packages").iterdir()):
        raise RuntimeError("Preparation requires fresh Nuplane state and an empty directory feed")
    source = root / "published-versions.json"
    if not source.exists():
        urllib.request.urlretrieve("https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/publish-state/published-versions.json", source)
    published = json.loads(source.read_text())
    versions = {x["package_id"]: x["version"] for x in published["packages"]}
    missing = [x for x in ROOTS if x not in versions]
    if missing:
        raise RuntimeError(f"Root IDs are not in the published record: {missing}")
    references = "\n".join(f'    <PackageReference Include="{x}" Version="[{versions[x]}]" />' for x in ROOTS)
    write(root / "restore" / "restore.csproj", '<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n  <ItemGroup>\n' + references + '\n  </ItemGroup>\n</Project>\n')
    write(root / "restore" / "NuGet.config", f'''<configuration><packageSources><clear />
<add key="elsa-4" value="{FEED}" />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
<add key="cshells" value="https://f.feedz.io/valence-works/cshells/nuget/index.json" />
<add key="bpmn" value="https://f.feedz.io/valence-works/bpmn/nuget/index.json" />
</packageSources></configuration>\n''')
    write(root / "roots.json", {"publishCommit": published["last_publish_commit"], "roots": {x: versions[x] for x in ROOTS}})
    features = {x: {} for x in [
        "Primitives", "Events", "Serialization", "Tasks", "Mediator", "Expressions", "MemoryCache",
        "FileSystemDistributedLocking", "ApiCapabilities", "ActivitiesRuntime", "ActivitiesDesignApi",
        "WorkflowsDesignApi", "WorkflowDesignValidations", "WorkflowsPublishing", "WorkflowsPublishingApi",
        "FoundationIdentityAbstractions", "FoundationIdentityApi",
    ]}
    for name in ["ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore"]:
        features[name] = {"Provider": "Sqlite", "ConnectionName": "Fr2Proof"}
    features["WorkflowsRuntimeEntityFrameworkCore"] = {"Provider": "Sqlite", "ConnectionName": "Fr2Proof"}
    features["IdentityIamEntityFrameworkCore"] = {"Provider": "Sqlite", "ConnectionName": "Fr2Proof"}
    features["WorkflowsRuntimeApi"] = {}
    features["WorkflowsRuntimeScheduling"] = {}
    features["WorkflowsRuntimeRecurringTriggers"] = {}
    features["FoundationIdentityAspNetCoreIdentity"] = {"IsDefault": True}
    features["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore"] = {
        "Provider": "Sqlite", "ConnectionName": "Fr2Proof", "IsDevelopmentOrDemo": False,
        "SeedAdminUserName": "fr2-admin", "SeedAdminEmail": "fr2@example.invalid", "SeedAdminRoleName": "administrator",
    }
    # Initially the catalog is empty. The live-reload leg adds the file-reconciliation feature and catalog.
    write(root / "config" / "shells.json", {"CShells": {"Shells": {"default": {"Name": "default", "Features": features, "Configuration": {"WebRouting": {"Path": ""}}}}}})
    write(root / "catalog" / "workflows.json", [])
    if not (root / "private.env").exists():
        policy_groups = (string.ascii_uppercase, string.ascii_lowercase, string.digits, "!@#%+")
        password = ''.join(secrets.choice(group) for group in policy_groups) + secrets.token_urlsafe(24)
        key = secrets.token_urlsafe(40)
        config = {
            "ASPNETCORE_ENVIRONMENT": "Production", "DOTNET_ENVIRONMENT": "Production",
            "DOTNET_USE_POLLING_FILE_WATCHER": "1",
            "ASPNETCORE_URLS": "https://+:8443", "ASPNETCORE_Kestrel__Certificates__Default__Path": "/proof/config/tls.pfx",
            "ASPNETCORE_Kestrel__Certificates__Default__Password": key,
            "ConnectionStrings__Fr2Proof": "Data Source=/proof/data/fr2.sqlite;Pooling=False",
            "Elsa__Persistence__EntityFramework__Migrate__Policy": "AutoMigrate",
            "Elsa__ModuleManagement__Enabled": "true", "Elsa__ModuleManagement__ApiKey": key,
            "Elsa__Boot__EagerShellActivation__Retry__MaxAttempts": "30",
            "CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword": password,
            "CShells__Shells__default__Features__WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey": key,
            "CShells__Shells__default__Features__WorkflowsRuntimeEntityFrameworkCore__HierarchyCursorSigningKey": key,
            "CShells__Shells__default__Features__WorkflowsRuntimeApi__ActivityExecutionHierarchyCursorSigningKey": key,
            "CShells__Shells__default__Features__WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId": "fr2",
            "CShells__Shells__default__Features__WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__fr2": __import__('base64').b64encode(secrets.token_bytes(32)).decode(),
        }
        write(root / "private.env", "".join(f"{k}={v}\n" for k, v in config.items()))
        (root / "private.env").chmod(0o600)
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "7", "-subj", "/CN=localhost", "-keyout", str(root / "config" / "tls.key"), "-out", str(root / "config" / "tls.crt")], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(["openssl", "pkcs12", "-export", "-out", str(root / "config" / "tls.pfx"), "-inkey", str(root / "config" / "tls.key"), "-in", str(root / "config" / "tls.crt"), "-passout", "stdin"], input=key, text=True, check=True)
    print(f"Prepared {len(ROOTS)} roots at {root}; private.env and TLS private material must not be published.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=pathlib.Path)
    args = parser.parse_args()
    prepare(args.root.resolve())
