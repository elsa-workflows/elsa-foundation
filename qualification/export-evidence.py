#!/usr/bin/env python3
"""Copy owned UX evidence for sharing, with transparent credential-only redaction.
Originals remain private. Recording IDs, source metadata, native outcomes and images are preserved.
"""
import argparse, base64, hashlib, io, json, re, shutil, zipfile
import ahocorasick
from importlib.metadata import version
from urllib.parse import parse_qsl, quote, quote_plus, unquote
from pathlib import Path, PurePosixPath
JWT = re.compile(r'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+')
AUTH_KEYS = {'password', 'seedadminpassword', 'accesstoken', 'refreshtoken', 'idtoken', 'clientsecret', 'authorization', 'cookie', 'setcookie'}
HEADERS = {'authorization', 'cookie', 'set-cookie'}
MARK = '[credential redacted]'
CONTROL_BYTES = re.compile(rb'[\x00-\x08\x0b\x0c\x0e-\x1a\x1c-\x1f\x7f]')
LINE_BREAKS = re.compile(r'[\n\r\v\f\x1c-\x1e\x85\u2028\u2029]')
# Exact immutable assets from the installed locked Playwright 1.61.1 distribution.
# They contain legitimate literal control characters in JavaScript. No other
# path/content pair is exempt from nominal-text control-byte rejection.
PLAYWRIGHT_TRACE_ASSETS = {
    'html/trace/uiMode.Ut8wwJNp.js': '0ab1e917250833c3f6b3ed3ae3f359158bf68cd2d9951163ee6edff9b8ed1e5b',
    'html/trace/assets/defaultSettingsView-BNmKHKpQ.js': 'f21839cd274944763d12ad24529345b9f8ebf339409f51f4dbe52f9d9baeb09c',
}
REPORT_OPEN = b'<template id="playwrightReportBase64">'
REPORT_CLOSE = b'</template>'
REPORT_URI = b'data:application/zip;base64,'
REPORT_LIMITS = {'encoded': 16 * 1024 * 1024, 'decoded': 12 * 1024 * 1024,
                 'members': 512, 'memberBytes': 64 * 1024 * 1024, 'expandedBytes': 128 * 1024 * 1024}

class _EmbeddedReport:
    def __init__(self, original, prefix, suffix, archive):
        self.original, self.prefix, self.suffix, self.archive = original, prefix, suffix, archive

class _SecretCandidates(list):
    """Find exact literals in one scan, preserving the original replacement order."""
    def __init__(self, values):
        super().__init__(values)
        self.text = ahocorasick.Automaton()
        self.binary = ahocorasick.Automaton()
        for index, value in enumerate(values):
            self.text.add_word(value, index)
            self.binary.add_word(value.encode('utf-8').decode('latin1'), index)
        if values:
            self.text.make_automaton(); self.binary.make_automaton()

    def has_text(self, value):
        return bool(self) and next(self.text.iter(value), None) is not None

    def has_bytes(self, value):
        # Latin-1 is a lossless byte-to-codepoint mapping, not UTF-8 decoding.
        return bool(self) and next(self.binary.iter(value.decode('latin1')), None) is not None

    def scrub_text(self, value):
        previous = -1
        while self:
            selected = None
            for _, index in self.text.iter(value):
                if index > previous and (selected is None or index < selected):
                    selected = index
                    if selected == previous + 1: break
            if selected is None: break
            value = value.replace(self[selected], MARK)
            previous = selected
            # Rescan after each real replacement: a marker/boundary can create
            # a later match. Earlier patterns must never be applied a second time.
        return value

def normalized(s): return re.sub('[^a-z]', '', s.lower())
def sha(b): return hashlib.sha256(b).hexdigest()
def documents(text):
    try: return ('json', json.loads(text))
    except (ValueError, RecursionError):
        lines = text.splitlines()
        try: return ('lines', [json.loads(line) for line in lines if line.strip()]) if lines else (None, None)
        except (ValueError, RecursionError): return (None, None)

def validate_file(data, name):
    path = PurePosixPath(name)
    if not name or path.is_absolute() or '..' in path.parts or '\\' in name or re.match(r'^[A-Za-z]:', name):
        raise ValueError('Unsafe archive path')
    forbidden = tuple(base + side for base in ('.db', '.sqlite', '.sqlite3') for side in ('', '-wal', '-shm', '-journal')) + ('.dmp', '.dump', '.heap', '.dll', '.pdb', '.exe', '.so', '.dylib', '.wasm', '.pyc')
    if name.lower().endswith(forbidden) or data.startswith(b'SQLite format 3\x00'):
        raise ValueError('Private store/binary is not shareable evidence')
    if data.startswith((b'MZ', b'\x7fELF', b'\x00asm', b'\xca\xfe\xba\xbe', b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xce\xfa\xed\xfe', b'\xfe\xed\xfa\xce')):
        raise ValueError('Executable binary is not shareable evidence')
    expected_asset = PLAYWRIGHT_TRACE_ASSETS.get(name)
    if expected_asset and sha(data) != expected_asset:
        raise ValueError('Trace viewer asset differs from its qualified distribution')
    if name.endswith('.zip'): return
    if data.startswith((b'\x89PNG\r\n\x1a\n', b'\xff\xd8\xff', b'GIF87a', b'GIF89a', b'wOFF', b'wOF2', b'OTTO', b'\x00\x01\x00\x00')): return
    try: data.decode('utf-8')
    except UnicodeDecodeError: raise ValueError('Unrecognized binary evidence')
    if CONTROL_BYTES.search(data) and not expected_asset:
        raise ValueError('Binary control bytes in nominal text evidence')


def expand(data, name='', depth=0, report_budget=None):
    if depth > 3: raise ValueError('Unexpected nested evidence archive')
    validate_file(data, name)
    if name.endswith('.zip'):
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            members = z.infolist()
            if report_budget is not None:
                report_budget['members'] += len(members)
                report_budget['expanded'] += sum(member.file_size for member in members)
                if report_budget['members'] > REPORT_LIMITS['members'] or any(member.file_size > REPORT_LIMITS['memberBytes'] for member in members) or report_budget['expanded'] > REPORT_LIMITS['expandedBytes']:
                    raise ValueError('Expanded report exceeds its shared recursive bound')
            names=z.namelist()
            if len(names)!=len(set(names)) or any((i.external_attr>>16)&0o170000 == 0o120000 for i in z.infolist()):
                raise ValueError('Duplicate or symlink archive member')
            return {n: expand(z.read(n), n, depth+1, report_budget) for n in names if not n.endswith('/')}
    carriers = re.findall(rb'data:application/zip;base64,', data, re.IGNORECASE)
    if carriers or name.lower().endswith('.html') and b'playwrightreportbase64' in data.lower():
        if len(carriers) != 1 or carriers[0] != REPORT_URI or data.count(REPORT_OPEN) != 1:
            raise ValueError('Unrecognized or duplicate encoded report carrier')
        start = data.index(REPORT_OPEN) + len(REPORT_OPEN)
        end = data.find(REPORT_CLOSE, start)
        if end < start or data[start:start+len(REPORT_URI)] != REPORT_URI:
            raise ValueError('Malformed encoded report carrier')
        start += len(REPORT_URI)
        encoded = data[start:end]
        if not encoded or len(encoded) > REPORT_LIMITS['encoded']:
            raise ValueError('Encoded report exceeds its bound')
        try: decoded = base64.b64decode(encoded, validate=True)
        except (ValueError, base64.binascii.Error): raise ValueError('Malformed encoded report base64')
        if base64.b64encode(decoded) != encoded or len(decoded) > REPORT_LIMITS['decoded']:
            raise ValueError('Noncanonical or oversized encoded report')
        budget = report_budget if report_budget is not None else {'members': 0, 'expanded': 0}
        return _EmbeddedReport(data, data[:start], data[end:], expand(decoded, 'embedded-report.zip', depth+1, budget))
    return data

def leaves(value):
    if isinstance(value, bytes): yield value
    elif isinstance(value, _EmbeddedReport):
        yield value.original
        yield from leaves(value.archive)
    else:
        for item in value.values(): yield from leaves(item)

def credential_strings(value):
    if isinstance(value,str): yield value
    elif isinstance(value,list):
        for item in value: yield from credential_strings(item)
    elif value is not None:
        raise ValueError('Unsupported credential representation; refuse sharing')

def collect_credential(value, secrets, kind=''):
    if normalized(kind)=='setcookie': kind='set-cookie'
    for text in credential_strings(value):
        if not text: continue
        secrets.add(text)
        if kind == 'authorization' and ' ' in text:
            secrets.add(text.split(' ',1)[1].strip())
        if kind in {'cookie','set-cookie'}:
            pieces=text.split(';') if kind=='cookie' else text.split(';')[:1]
            for piece in pieces:
                if '=' in piece: secrets.add(piece.split('=',1)[1].strip())

def collect_form(text, secrets):
    # Real browser login traces can store URL-encoded POST bytes as a resource.
    if '=' in text and len(text) <= 262144 and re.fullmatch(r'[^&=\r\n]{1,128}=[^&\r\n]*(?:&[^&=\r\n]{1,128}=[^&\r\n]*)*', text):
        for key, value in parse_qsl(text, keep_blank_values=True):
            if normalized(key) in AUTH_KEYS: collect_credential(value, secrets, key)

def collect(value, secrets):
    if isinstance(value, dict):
        if normalized(str(value.get('name', ''))) in AUTH_KEYS:
            collect_credential(value.get('value'),secrets,str(value['name']).lower())
        # Playwright storageState cookie records have name/value/domain/path.
        if {'name','value','domain','path'} <= value.keys() and isinstance(value['value'], str): secrets.add(value['value'])
        for key, item in value.items():
            if normalized(key) in AUTH_KEYS: collect_credential(item,secrets,key.lower())
            collect(item, secrets)
    elif isinstance(value, list):
        for item in value: collect(item, secrets)
    elif isinstance(value, str):
        # A single JSON primitive cannot contain a credential field. Quoted
        # strings, containers and every possible multiline JSONL still recurse.
        if value.lstrip().startswith(('{', '[', '"')) or LINE_BREAKS.search(value):
            kind, nested = documents(value)
            if kind:
                collect(nested, secrets)
                return
        collect_form(value, secrets)

def scrub(value, secrets):
    if isinstance(value, dict):
        credential = normalized(str(value.get('name',''))) in AUTH_KEYS or {'name','value','domain','path'} <= value.keys()
        return {k: MARK if normalized(k) in AUTH_KEYS or credential and k == 'value' else scrub(v,secrets) for k,v in value.items()}
    if isinstance(value,list): return [scrub(v,secrets) for v in value]
    if isinstance(value,str):
        if value.lstrip().startswith(('{','[')):
            kind,nested=documents(value)
            if kind: return json.dumps(scrub(nested,secrets),separators=(',',':'))
        value=JWT.sub(MARK,value)
        if isinstance(secrets, _SecretCandidates): return secrets.scrub_text(value)
        for secret in secrets:
            value=value.replace(secret,MARK)
        return value
    return value

def render(value, secrets):
    if isinstance(value, _EmbeddedReport):
        rebuilt = value.prefix + base64.b64encode(render(value.archive, secrets)) + value.suffix
        return scrub(rebuilt.decode('utf-8'), secrets).encode('utf-8')
    if isinstance(value,dict):
        buf=io.BytesIO()
        with zipfile.ZipFile(buf,'w',zipfile.ZIP_DEFLATED) as z:
            for n,v in value.items(): z.writestr(n,render(v,secrets))
        return buf.getvalue()
    try: text=value.decode('utf-8')
    except UnicodeDecodeError: return value
    kind,parsed=documents(text)
    if kind=='json': return (json.dumps(scrub(parsed,secrets),separators=(',',':'))+'\n').encode()
    if kind=='lines': return ('\n'.join(json.dumps(scrub(v,secrets),separators=(',',':')) for v in parsed)+'\n').encode()
    return scrub(text,secrets).encode()

def safe_output_name(name, secrets):
    candidates = {name}
    for _ in range(3): candidates.update(unquote(value) for value in tuple(candidates))
    if any(JWT.search(value) or (secrets.has_text(value) if isinstance(secrets, _SecretCandidates) else any(secret in value for secret in secrets)) for value in candidates):
        raise ValueError('Credential in an output path or archive member; refuse sharing')

def safe_tree_names(tree, secrets):
    if isinstance(tree, _EmbeddedReport):
        safe_tree_names(tree.archive, secrets)
    if isinstance(tree, dict):
        for name, item in tree.items():
            safe_output_name(name, secrets)
            safe_tree_names(item, secrets)

def no_credential_residue(data, secrets):
    # Binary assets cannot be rewritten without altering the original images/fonts.
    if re.search(rb'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', data) or (secrets.has_bytes(data) if isinstance(secrets, _SecretCandidates) else any(secret.encode('utf-8') in data for secret in secrets)):
        raise ValueError('Credential residue; refuse sharing')

def export(source,destination):
    if version('pyahocorasick') != '2.2.0': raise ValueError('Use the qualified pinned matcher dependency')
    source=source.resolve(strict=True);destination=destination.resolve()
    if destination.exists() or destination.is_relative_to(source) or source.is_relative_to(destination): raise ValueError('Use a new separate export directory')
    allowed={'.png','.json','.yml','.yaml','.html','.js','.css','.svg','.txt','.log','.zip','.mjs','.py','.ttf','.webmanifest'}
    originals={}
    for p in sorted(source.rglob('*')):
        if p.is_symlink(): raise ValueError('Symbolic link is not shareable evidence')
        if not p.is_file(): continue
        if p.suffix.lower() not in allowed: raise ValueError('Unapproved evidence file')
        originals[str(p.relative_to(source))]=p.read_bytes()
    expanded={n:expand(v,n) for n,v in originals.items()};secrets=set()
    print(json.dumps({'phase':'expanded','files':len(originals)}), flush=True)
    for tree in expanded.values():
        for data in leaves(tree):
            try: text=data.decode('utf-8')
            except UnicodeDecodeError: continue
            kind,parsed=documents(text)
            if kind: collect(parsed,secrets)
            else: collect_form(text,secrets)
    secrets.discard('');secrets.discard(MARK)
    if any(len(s)<8 for s in secrets):
        raise ValueError('Short credential cannot safely be removed from unstructured evidence; refuse sharing')
    variants=set(secrets)
    for secret in secrets:
        for encoded in (quote(secret, safe=''), quote_plus(secret)):
            variants.add(encoded)
            variants.add(re.sub(r'%[0-9A-F]{2}', lambda match: match[0].lower(), encoded))
    secrets=_SecretCandidates(sorted(variants,key=len,reverse=True))
    print(json.dumps({'phase':'credential-collection-complete','variantCount':len(secrets)}), flush=True)
    prepared={};manifest=[]
    for name,tree in expanded.items():
        safe_output_name(name, secrets);safe_tree_names(tree, secrets)
        result=render(tree,secrets)
        if name in PLAYWRIGHT_TRACE_ASSETS and result != originals[name]:
            raise ValueError('Qualified trace viewer asset cannot be credential-scrubbed')
        for data in leaves(expand(result,name)): no_credential_residue(data,secrets)
        prepared[name]=result
        manifest.append({'path':name,'originalSha256':sha(originals[name]),'sharedSha256':sha(result),'changed':result!=originals[name]})
    metadata=(json.dumps({'kind':'credential-only shared copy; original evidence retained privately','credentialsPrinted':False,'fileManifest':manifest,'exporterSha256':sha(Path(__file__).read_bytes()),'literalMatcher':{'package':'pyahocorasick','version':version('pyahocorasick'),'semantics':'Original longest-first sequential replacements; rescan after each real replacement'},'encodedReportPolicy':{'carrier':'Observed Playwright template ZIP data URL; recursively validate, collect, scrub, reencode and revalidate','limits':REPORT_LIMITS},'qualifiedTraceViewerAssets':[{ 'path':name, 'sha256':sha(data), 'distribution':'Playwright 1.61.1' } for name,data in originals.items() if PLAYWRIGHT_TRACE_ASSETS.get(name) == sha(data)],'traceResourceNames':'Preserved so trace references resolve; content hashes in resource names predate disclosed redaction'},indent=2)+'\n').encode()
    no_credential_residue(metadata,secrets)
    destination.mkdir(parents=True)
    try:
        for name,result in prepared.items():
            target=destination/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(result)
        (destination/'credential-redaction.json').write_bytes(metadata)
    except BaseException:
        shutil.rmtree(destination)
        raise
    print(json.dumps({'files':len(manifest),'changedFiles':sum(i['changed'] for i in manifest),'credentialValuesPrinted':False}))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('destination',type=Path);a=p.parse_args();export(a.source,a.destination)
