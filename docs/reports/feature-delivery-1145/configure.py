#!/usr/bin/env python3
"""Convert the restored closure into explicit cold-start feed pins, preserving image sharing."""
import argparse
import json
import pathlib
import re


def configure(root):
    assets = json.loads((root / "restore/obj/project.assets.json").read_text())
    deps = json.loads((root / "host.deps.json").read_text())
    host = {k.split('/')[0].lower(): k.split('/')[1] for k in deps['libraries']}
    shared = {x.lower() for x in json.loads((root / 'host.production.json').read_text())['Nuplane']['HostProvidedPackages']}
    inventory = []
    for key in assets['libraries']:
        name, version = key.split('/')
        metadata = json.loads((root / 'restore/pkgs' / name.lower() / version.lower() / '.nupkg.metadata').read_text())
        provided = name.lower() in shared or (name.lower() in host and not name.startswith('Elsa.'))
        inventory.append(dict(id=name, version=version, source=metadata['source'], hostProvided=provided))
    (root / 'package-closure.json').write_text(json.dumps(inventory, indent=2) + '\n')
    original = re.sub(r'^\s*//.*$', '', (root / 'host.appsettings.json').read_text(), flags=re.M)
    config = json.loads(original)
    nuplane = config['Nuplane']
    feeds = []
    for name, source in [('elsa-4', 'https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json'), ('nuget.org', 'https://api.nuget.org/v3/index.json')]:
        patterns = [f"{x['id']} [{x['version']}]" for x in inventory if not x['hostProvided'] and x['source'] == source and x['id'] != 'Elsa.Workflows.Design.Reconciliation.Json']
        feeds.append(dict(Name=name, ServiceIndex=source, IncludePatterns=patterns))
    feeds.append(dict(Name='directory-witness', DirectoryPath='/proof/packages', IncludePatterns=['Fr2Proof.*'], Directory=dict(Watch=True, DebounceWindow='00:00:01')))
    nuplane['Setup'] = dict(AutomaticReconciliation=True, PollInterval='00:10:00', StateFilePath='/app/.nuplane/state.json', Feeds=feeds)
    nuplane['FeedResolution'] = dict(PackageInstallRoot='/app/.nuplane/packages')
    nuplane['Capabilities'] = {'ef-provider': 'Sqlite'}
    nuplane['Loading']['DefaultLoadMode'] = 'HostIntegrated'
    nuplane['Convergence'] = dict(Manifest=dict(Enabled=True, Path='/proof/config/desired.json', SchemaVersion='1.0'))
    nuplane['LockFile'] = dict(Enabled=False)
    config['Logging']['LogLevel']['Nuplane'] = 'Debug'
    config['Logging']['LogLevel']['CShells'] = 'Information'
    config['Logging']['LogLevel']['Microsoft.EntityFrameworkCore.Database.Command'] = 'Warning'
    (root / 'config/appsettings.json').write_text(json.dumps(config, indent=2) + '\n')
    (root / 'config/desired.json').write_text(json.dumps(dict(SchemaVersion='1.0', Packages=[]), indent=2) + '\n')
    print(f"Configured {sum(len(x['IncludePatterns']) for x in feeds[:-1])} remote pins; live Json reconciliation package excluded.")


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=pathlib.Path)
    configure(parser.parse_args().root.resolve())
