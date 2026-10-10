import pathlib, sys, json, hashlib, xml.etree.ElementTree as ET, os

base = pathlib.Path(__file__).parent
assert len(sys.argv) == 5, (
    'Usage: audit-actual-host-trx.py <trx> <output-name.json> '
    '<foundation-asset-audit.json> <workbench-asset-audit.json>'
)
trx = pathlib.Path(sys.argv[1])
dest = base / sys.argv[2]
foundation_audit_path = base / sys.argv[3]
workbench_audit_path = base / sys.argv[4]
assert not dest.exists(), 'Preserve earlier evidence'
assert trx.is_file(), str(trx)

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
root = ET.parse(trx).getroot()
results = []
expected_hosts = {'Elsa.Foundation.Host', 'Elsa.Workbench'}

for node in root.findall('.//t:UnitTestResult', ns):
    if 'Actual_' not in node.attrib['testName']:
        continue
    assert node.attrib['outcome'] == 'Passed', node.attrib['testName']
    stdout_node = node.find('t:Output/t:StdOut', ns)
    assert stdout_node is not None and stdout_node.text
    stdout = stdout_node.text
    mapped = [
        json.loads(line.removeprefix('HOST_ASSEMBLY_EVIDENCE '))
        for line in stdout.splitlines() if line.startswith('HOST_ASSEMBLY_EVIDENCE ')
    ]
    assert len(mapped) == 1
    evidence = mapped[0]
    host = evidence['host']
    pid = evidence['processId']
    if host == 'Elsa.Foundation.Host':
        audit_path = foundation_audit_path
    elif host == 'Elsa.Workbench':
        audit_path = workbench_audit_path
    else:
        raise AssertionError(('Unexpected actual host', host))

    audit = json.loads(audit_path.read_text())
    assert audit['accepted'] is True
    expected = {
        member['dll']: member['sha256']
        for package in audit['packages']
        for member in package['matchedRuntimeAssets']
    }
    actual = {member['path']: member['sha256'] for member in evidence['assemblies']}
    assert actual == expected, (host, actual.keys() ^ expected.keys())
    for path, digest in actual.items():
        assert hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest() == digest, path

    phases = [line for line in stdout.splitlines() if line.startswith('HOST_READABILITY ')]
    for phase in [
        'v1-active', 'v1-shell', 'held-request', 'v2-serving-held-v1',
        'v2-shell-candidate', 'old-provider-drained',
        'old-provider-drained-operation', 'zero-active-packages', 'v2-reintroduced'
    ]:
        assert any('phase=' + phase + ' ' in line for line in phases), (host, phase)
    assert any(
        'phase=v2-shell-candidate ' in line and 'ordersStatus=409' in line
        for line in phases
    ), host
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        gone = True
    else:
        gone = False
    assert gone, (host, pid, 'Owned child has not exited')
    results.append({
        'test': node.attrib['testName'],
        'outcome': 'Passed',
        'host': host,
        'pid': pid,
        'ownedProcessGone': gone,
        'mappedDlls': evidence['assemblies'],
        'phases': phases
    })

assert len(results) == 2, len(results)
assert {result['host'] for result in results} == expected_hosts
assert len({result['test'] for result in results}) == 2
assert sum(len(result['mappedDlls']) for result in results) == 25
dest.write_text(json.dumps({
    'accepted': True,
    'trx': str(trx),
    'trxSha256': hashlib.sha256(trx.read_bytes()).hexdigest(),
    'evidenceScope': (
        'Both stable actual child hosts passed the generation hold, replacement, drain, zero-package and '
        'reintroduction scenario; their mapped upstream DLL path/hash pairs match the stable public package '
        'asset audits (25 pairs total), and both owned child PIDs exited. Fresh stable-cache restore proof '
        'is separate and is not established by this TRX audit.'
    ),
    'results': results
}, indent=2) + '\n')
print(json.dumps({
    'accepted': True,
    'hosts': len(results),
    'mappedDlls': sum(len(result['mappedDlls']) for result in results),
    'ownedPids': [result['pid'] for result in results]
}))

