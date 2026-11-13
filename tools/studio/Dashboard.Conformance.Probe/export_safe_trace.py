#!/usr/bin/env python3
"""Export Playwright v8 browser traces without credentials or executable source.

Raw traces remain private. Only reconstructed browser action/DOM records and referenced
static rendering assets enter the output. Network bodies, API results, test runner data,
source files and authentication screenshots are deliberately omitted. Open the result in
Trace Viewer and inspect its usability before publishing it as qualification evidence.
"""
from __future__ import annotations
import argparse
import base64
import hashlib
import io
import json
import re
import stat
import struct
import zlib
import zipfile
from collections import Counter
from pathlib import Path, PurePosixPath
from urllib.parse import quote, urlsplit, urlunsplit

MAX_TOTAL = 512 * 1024 * 1024
MAX_ENTRY = 32 * 1024 * 1024
TOKEN = re.compile(r'(?:eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,})')
SENSITIVE = re.compile(r'(?:password|authorization|cookie|token|secret|api[_-]?key|management[_-]?key|private[_-]?key|cursor)$', re.I)
TYPES = {
    'context-options': {'version','type','origin','browserName','playwrightVersion','options','platform','wallTime','monotonicTime','sdkLanguage','testIdAttributeName','contextId','title','testTimeout'},
    'before': {'type','callId','startTime','class','method','params','stepId','pageId','beforeSnapshot','title','stack','parentId','group'},
    'after': {'type','callId','endTime','result','afterSnapshot','error','attachments','annotations','point'},
    'event': {'type','time','class','method','params','pageId'},
    'frame-snapshot': {'type','snapshot'},
    'screencast-frame': {'type','pageId','sha1','width','height','timestamp','frameSwapWallTime'},
    'input': {'type','callId','inputSnapshot','point'},
    'log': {'type','callId','time','message'},
    'console': {'type','messageType','text','args','location','time','pageId'},
    'resource-snapshot': {'type','snapshot'},
}
STATIC = {'text/css', 'image/png', 'image/jpeg', 'image/webp', 'image/gif', 'image/x-icon', 'font/woff', 'font/woff2', 'application/font-woff', 'application/vnd.ms-fontobject', 'font/ttf', 'font/otf'}
SAFE_OPTIONS = {'viewport','deviceScaleFactor','isMobile','hasTouch','locale','colorScheme','reducedMotion','forcedColors','contrast','noDefaultViewport'}
URL_ATTRIBUTES = {'src','href','action','formaction','poster','__playwright_current_src__','cite','background','longdesc','data','xlink:href'}

class UnsafeTrace(ValueError):
    """Only fixed, non-secret diagnostic messages may escape this exporter."""


def read_archive(path: Path) -> dict[str, bytes]:
    result = {}
    with zipfile.ZipFile(path) as archive:
        total = 0
        for info in archive.infolist():
            name = info.filename
            parts = PurePosixPath(name).parts
            if name in result or not parts or name.startswith('/') or '\\' in name or any(p in {'.','..'} for p in parts) or stat.S_ISLNK(info.external_attr >> 16):
                raise UnsafeTrace('Unsafe or duplicate archive path')
            total += info.file_size
            if info.file_size > MAX_ENTRY or total > MAX_TOTAL:
                raise UnsafeTrace('Archive exceeds export bounds')
            if not info.is_dir(): result[name] = archive.read(info)
    return result


def objects(data: bytes):
    try:
        for line in data.splitlines():
            if not line.strip(): continue
            record = json.loads(line)
            if not isinstance(record, dict) or record.get('type') not in TYPES or set(record) - TYPES[record['type']]:
                raise UnsafeTrace('Unknown trace record schema')
            yield record
    except (UnicodeError, json.JSONDecodeError):
        raise UnsafeTrace('Invalid trace JSON') from None


def normalized_url(value: str) -> str:
    try:
        url = urlsplit(value)
        if url.scheme in {'http','https'} and url.hostname in {'localhost','127.0.0.1','[::1]','::1'}:
            # Qualification hosts are local; omit userinfo, query and fragments.
            host = url.hostname
            if ':' in host: host = '[' + host + ']'
            return urlunsplit((url.scheme, host + (':' + str(url.port) if url.port else ''), url.path, '', ''))
        if not url.scheme and not url.netloc:
            return urlunsplit(('', '', url.path, '', ''))
        if value in {'about:blank','about:srcdoc'}: return value
    except ValueError: pass
    return 'about:blank'


class Exporter:
    def __init__(self, entries: dict[str, bytes]):
        self.entries = entries
        self.secrets: set[str] = set()
        self.counts = Counter()
        self.resources: dict[str, bytes] = {}
        self.static_hashes: set[str] = set()
        self.mime: dict[str,str] = {}
        self.contexts: set[str] = set()
        self.inventory()
        forms=set()
        for secret in self.secrets:
            forms.update({secret,quote(secret,safe=''),base64.b64encode(secret.encode()).decode(),base64.urlsafe_b64encode(secret.encode()).decode()})
        self.forms=sorted(forms,key=len,reverse=True)
        self.prefix=re.compile("|".join(re.escape(prefix) for prefix in sorted({form[:4] for form in self.forms}))) if self.forms else None
        self.byte_secrets=[form.encode() for form in self.forms]

    def discover(self, value, key=''):
        if isinstance(value, dict):
            for k, v in value.items():
                if SENSITIVE.search(k) and isinstance(v, str) and v:
                    self.secrets.add(v)
                self.discover(v, k)
            # Opaque tokens need not look like JWTs. Inventory their bare values too.
            name=str(value.get('name','')).lower()
            pair=value.get('value')
            if isinstance(pair,str) and pair:
                if key=='cookies' or SENSITIVE.search(name): self.secrets.add(pair)
                if name=='authorization':
                    parts=pair.split(None,1)
                    if len(parts)==2: self.secrets.add(parts[1])
                if name in {'cookie','set-cookie'}:
                    for cookie in (pair.split(';')[:1] if name=='set-cookie' else pair.split(';')):
                        _,sep,token=cookie.strip().partition('=')
                        if sep and token:self.secrets.add(token)
        elif isinstance(value, list):
            for item in value: self.discover(item, key)
        elif isinstance(value, str):
            self.secrets.update(TOKEN.findall(value))
            if value[:1] in {'{','['}:
                try: self.discover(json.loads(value))
                except (ValueError, RecursionError): pass
            if key == 'binary' and len(value) < MAX_ENTRY:
                try:
                    decoded = base64.b64decode(value, validate=True).decode('utf-8')
                    self.discover(decoded)
                except (ValueError, UnicodeError): pass

    def inventory(self):
        for name, data in self.entries.items():
            if name.endswith(('.trace','.network')):
                for record in objects(data):
                    self.discover(record)
                    if record['type'] == 'before' and record.get('method') == 'fill':
                        params = record.get('params',{})
                        if re.search('password|secret|token', str(params.get('selector','')), re.I) and isinstance(params.get('value'),str) and params['value']:
                            self.secrets.add(params['value'])
                    if record['type'] == 'resource-snapshot':
                        content = record['snapshot'].get('response',{}).get('content',{})
                        mime = content.get('mimeType','').split(';')[0].lower()
                        digest = content.get('_sha1')
                        if mime in STATIC and isinstance(digest,str): self.static_hashes.add(digest);self.mime[digest]=mime
            elif name.startswith('resources/'):
                try: self.discover(data.decode('utf-8'))
                except UnicodeError: pass
        # Short credential values cannot be safely substituted throughout arbitrary evidence.
        if any(len(s) < 4 for s in self.secrets):
            raise UnsafeTrace('Credential too short for safe evidence export')

    def text(self, value: str) -> str:
        # Most DOM strings cannot contain a known credential. Every known form has at
        # least four characters; this prefilter avoids hundreds of redundant replacements.
        if len(value)<4 or self.prefix is None or not self.prefix.search(value):
            return TOKEN.sub('[redacted]',value)
        for form in self.forms:
            value = value.replace(form, '[redacted]')
        return TOKEN.sub('[redacted]',value)

    def clean(self, value):
        if isinstance(value,str): return self.text(value)
        if isinstance(value,list): return [self.clean(x) for x in value]
        if isinstance(value,dict): return {self.text(k): self.clean(v) for k,v in value.items() if not SENSITIVE.search(k)}
        return value

    def css(self,text):
        text_for_policy=re.sub(r'\\\r?\n','',re.sub(r'/\*.*?\*/','',text,flags=re.S))
        decoded=re.sub(r'\\([0-9a-fA-F]{1,6})\s?',lambda match:chr(int(match[1],16)) if int(match[1],16)<=0x10ffff else '',text_for_policy)
        decoded=re.sub(r'\\([^\r\n])',r'\1',decoded)
        if self.text(decoded)!=decoded or re.search(r'javascript\s*:|@import|(?:image-set|cross-fade|image)\s*\(|<\s*(?:script|html|svg)',decoded,re.I):return None
        for match in re.finditer(r'url\(\s*[\'\"]?([^\)\'\"]+)',decoded,re.I):
            url=match[1].strip()
            if url.startswith('data:font/woff2;base64,'):
                try:font=base64.b64decode(url.partition(',')[2],validate=True)
                except ValueError:return None
                if not self.font_format(font) or any(secret in font for secret in self.byte_secrets):return None
            elif normalized_url(url)!=url:return None
        without_fonts=re.sub(r'url\(\s*[\'\"]?data:font/woff2;base64,[A-Za-z0-9+/=]+[\'\"]?\s*\)','',decoded,flags=re.I)
        if re.search(r'data\s*:',without_fonts,re.I):return None
        return text

    @staticmethod
    def font_format(data):
        if len(data)<48 or data[:4] not in {b'wOFF',b'wOF2'} or struct.unpack('>I',data[8:12])[0]!=len(data):return False
        first=24 if data[:4]==b'wOFF' else 28
        return not any(struct.unpack('>I',data[offset:offset+4])[0] for offset in range(first,first+20,4))

    @staticmethod
    def image_format(data):
        if data.startswith(b'\x89PNG\r\n\x1a\n'):
            offset=8;seen=[]
            while offset+12<=len(data):
                size=struct.unpack('>I',data[offset:offset+4])[0];kind=data[offset+4:offset+8]
                end=offset+12+size
                if end>len(data) or zlib.crc32(data[offset+4:end-4])&0xffffffff!=struct.unpack('>I',data[end-4:end])[0]:return False
                if kind in {b'tEXt',b'zTXt',b'iTXt',b'eXIf'}:return False
                seen.append(kind);offset=end
                if kind==b'IEND':return offset==len(data) and seen[0]==b'IHDR' and b'IDAT' in seen
            return False
        if data.startswith(b'\xff\xd8'):
            # Validate JPEG segment framing and entropy markers; reject metadata carriers.
            offset=2;seen_frame=False
            while offset<len(data):
                if data[offset]!=255:return False
                while offset<len(data) and data[offset]==255:offset+=1
                if offset>=len(data):return False
                marker=data[offset];offset+=1
                if marker==0xd9:return seen_frame and offset==len(data)
                if marker in range(0xd0,0xd8) or marker==0x01:continue
                if marker==0xfe or marker in range(0xe1,0xf0):return False
                if offset+2>len(data):return False
                size=struct.unpack('>H',data[offset:offset+2])[0]
                if size<2 or offset+size>len(data):return False
                if marker in {0xc0,0xc1,0xc2}:seen_frame=True
                offset+=size
                if marker==0xda:
                    while offset<len(data)-1:
                        if data[offset]!=255:offset+=1;continue
                        following=data[offset+1]
                        if following==0 or following in range(0xd0,0xd8):offset+=2;continue
                        break
            return False
        return False

    def resource(self, digest: str, *, image=False) -> bool:
        if not isinstance(digest,str) or not (re.fullmatch(r'[a-f0-9]{40}(?:\.[a-z0-9]+)?',digest) or image and re.fullmatch(r'page@[a-f0-9]{32}-[0-9]+\.jpeg',digest)):
            raise UnsafeTrace('Invalid resource identity')
        if not image and digest not in self.static_hashes:return False
        path='resources/'+digest;data=self.entries.get(path)
        if data is None:raise UnsafeTrace('Missing referenced rendering resource')
        if any(secret in data for secret in self.byte_secrets):return False
        mime=self.mime.get(digest,'')
        if image or mime.startswith('image/'):
            if not self.image_format(data):return False
        elif mime=='text/css':
            try:text=data.decode('utf-8')
            except UnicodeError:return False
            if self.css(text) is None:return False
        elif mime in {'font/woff','application/font-woff','font/woff2'}:
            if not self.font_format(data):return False
        else:return False
        self.resources[path]=data
        return True

    def dom(self,node):
        if isinstance(node,str): return self.text(node)
        if not isinstance(node,list): raise UnsafeTrace('Unknown DOM snapshot node')
        # Incremental snapshot references have shape [[snapshotDelta,nodeIndex]].
        if len(node)==1 and isinstance(node[0],list) and len(node[0])==2 and all(isinstance(i,int) and i>=0 for i in node[0]): return node
        if len(node)==1 and isinstance(node[0],str): return ['TEMPLATE' if node[0].lower()=='script' else node[0]]
        if len(node)<2 or not isinstance(node[0],str) or not isinstance(node[1],dict): raise UnsafeTrace('Unknown DOM snapshot element')
        tag,attrs=node[0],node[1]
        private=tag.lower()=='input' and (str(attrs.get('type','')).lower()=='password' or re.search('password|secret|token',str(attrs.get('name',''))+str(attrs.get('id','')),re.I))
        sanitized={}
        for key,value in attrs.items():
            if not isinstance(value,str): raise UnsafeTrace('Unknown DOM attribute value')
            if key.lower().startswith('on') or key.lower() in {'srcdoc','nonce','integrity'}: continue
            if private and key in {'value','__playwright_value_'}: sanitized[key]='[redacted]'
            elif SENSITIVE.search(key): continue
            elif key.lower() in {'srcset','imagesrcset','ping','archive'}: continue
            elif key in URL_ATTRIBUTES: sanitized[key]=self.text(normalized_url(value))
            elif key=='style': sanitized[key]=self.css(value) or ''
            else: sanitized[key]=self.text(value)
        # Keep node ordinals and backreferences stable while removing executable content.
        children=[self.dom(child) for child in node[2:]]
        if tag.lower()=='style':children=[(self.css(c) or '') if isinstance(c,str) else c for c in children]
        if tag.lower()=='script':
            tag='TEMPLATE'; children=['' if isinstance(c,str) else c for c in children];sanitized={}
        return [tag,sanitized,*children]

    def browser_records(self,name: str,data: bytes):
        safe=[]; page_safe={}; known_snapshots=set(); wanted_snapshots=set(); context=False
        for record in objects(data):
            kind=record['type']
            if kind=='context-options':
                if record.get('version')!=8: raise UnsafeTrace('Only Playwright trace version 8 is supported')
                context=True
                current={k:record[k] for k in ('version','type','origin','browserName','playwrightVersion','platform','wallTime','monotonicTime','sdkLanguage','testIdAttributeName','contextId') if k in record}
                current['options']={k:self.clean(v) for k,v in record.get('options',{}).items() if k in SAFE_OPTIONS}
                if record.get('contextId'): self.contexts.add(record['contextId'])
            elif kind=='before':
                current={k:record[k] for k in ('type','callId','startTime','class','method','stepId','pageId','beforeSnapshot') if k in record}
                params=record.get('params',{})
                allowed={'selector','strict','timeout','key','state','waitUntil','viewportSize','fullPage','animations','type','mode','depth','boxes'}
                current['params']={k:self.clean(v) for k,v in params.items() if k in allowed}
                if 'url' in params:current['params']['url']=normalized_url(params['url'])
                if record.get('method')=='fill':
                    current['params']['value']='[redacted]' if re.search('password|secret|token',str(params.get('selector','')),re.I) else self.clean(params.get('value',''))
                if record.get('class')=='APIRequestContext': current['params']={k:v for k,v in current['params'].items() if k in {'url','timeout'}}
                if 'beforeSnapshot' in current: wanted_snapshots.add(current['beforeSnapshot'])
                self.counts['actions']+=1
            elif kind=='after':
                current={k:record[k] for k in ('type','callId','endTime','afterSnapshot','point') if k in record}
                if 'error' in record: current['error']={'message':'Action failed; inspect the reviewed safe report.'}
                if 'afterSnapshot' in current:wanted_snapshots.add(current['afterSnapshot'])
            elif kind=='frame-snapshot':
                source=record['snapshot']
                allowed={'callId','snapshotName','pageId','frameId','frameUrl','html','viewport','timestamp','wallTime','collectionTime','resourceOverrides','isMainFrame','doctype'}
                if set(source)-allowed:raise UnsafeTrace('Unknown frame snapshot schema')
                snap={k:source[k] for k in allowed-{'html','frameUrl','resourceOverrides'} if k in source}
                snap['html']=self.dom(source['html']);snap['frameUrl']=normalized_url(source['frameUrl']);snap['resourceOverrides']=[]
                known_snapshots.add(source['snapshotName'])
                if source.get('isMainFrame'):
                    raw=json.dumps(source['html'])
                    page_safe[source['pageId']]=snap['frameUrl'].startswith(('http://localhost','http://127.0.0.1')) and '/identity/' not in snap['frameUrl'] and not re.search(r'"type"\s*:\s*"password"',raw,re.I)
                current={'type':kind,'snapshot':snap};self.counts['snapshots']+=1
            elif kind=='screencast-frame':
                if not page_safe.get(record['pageId'],False):self.counts['omittedAuthenticationFrames']+=1;continue
                if not self.resource(record['sha1'],image=True):self.counts['omittedUnsafeRenderingAssets']+=1;continue
                current=record
            elif kind=='input':
                current={k:record[k] for k in ('type','callId','inputSnapshot','point') if k in record}
                if 'inputSnapshot' in current:wanted_snapshots.add(current['inputSnapshot'])
            elif kind=='event':
                if record.get('class')!='BrowserContext' or record.get('method')!='page':continue
                current={k:record[k] for k in ('type','time','class','method') if k in record}
                current['params']={'pageId':record['params']['pageId']}
            else:
                self.counts['omittedDiagnosticRecords']+=1;continue
            safe.append(self.clean(current))
        if not context:raise UnsafeTrace('Browser context metadata missing')
        # Playwright itself may emit a named snapshot which navigation prevented recording.
        # Retain the action, omit that unresolved link and count it; never invent a snapshot.
        for item in safe:
            for key in ('beforeSnapshot','afterSnapshot','inputSnapshot'):
                if key in item and item[key] not in known_snapshots:
                    del item[key];self.counts['omittedUnrecordedSnapshotLinks']+=1
        return b''.join(json.dumps(x,separators=(',',':'),ensure_ascii=False).encode()+b'\n' for x in safe)

    def network_records(self,data):
        safe=[]
        for record in objects(data):
            if record['type']!='resource-snapshot':raise UnsafeTrace('Unexpected network record')
            source=record['snapshot'];request=source.get('request',{});response=source.get('response',{})
            content=response.get('content',{});digest=content.get('_sha1')
            safe_content={k:content[k] for k in ('size','mimeType') if k in content}
            if digest and self.resource(digest):safe_content['_sha1']=digest
            current={k:source[k] for k in ('pageref','startedDateTime','time','timings','_frameref','_monotonicTime','_resourceType','_apiRequest') if k in source}
            current.update(request={'method':request.get('method','GET'),'url':normalized_url(request.get('url','')),'httpVersion':request.get('httpVersion','HTTP/1.1'),'headers':[],'cookies':[],'queryString':[],'headersSize':-1,'bodySize':0},
                response={'status':response.get('status',0),'statusText':'','httpVersion':response.get('httpVersion','HTTP/1.1'),'headers':[],'cookies':[],'content':safe_content,'redirectURL':'','headersSize':-1,'bodySize':0},cache={})
            safe.append({'type':'resource-snapshot','snapshot':self.clean(current)})
            self.counts['networkSummaries']+=1
        return b''.join(json.dumps(x,separators=(',',':')).encode()+b'\n' for x in safe)

    def export(self):
        output={}
        for name,data in self.entries.items():
            # Test runner traces duplicate parameters/results and embedded source. Browser traces
            # independently contain ordered owner and reader actions and their DOM snapshots.
            if name=='test.trace':self.counts['omittedRunnerTraces']+=1
            elif name.endswith('.trace'):output[name]=self.browser_records(name,data)
            elif name.endswith('.network'):output[name]=self.network_records(data)
        if not self.counts['actions'] or not self.counts['snapshots']:raise UnsafeTrace('Trace has no usable browser experience')
        output.update(self.resources)
        for name,data in output.items():
            if any(secret in data for secret in self.byte_secrets):raise UnsafeTrace('Secret remains in exported resource')
            try:text=data.decode('utf-8')
            except UnicodeError:continue
            if TOKEN.search(text) or any(secret in text for secret in self.secrets):raise UnsafeTrace('Secret remains in exported evidence')
        self.counts['contexts']=len(self.contexts);self.counts['renderingResources']=len(self.resources)
        return output


def export_trace(source: Path,destination: Path,source_sha: str):
    if not re.fullmatch('[a-f0-9]{40}',source_sha):raise UnsafeTrace('An exact source SHA is required')
    exporter=Exporter(read_archive(source));entries=exporter.export()
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,data in sorted(entries.items()):archive.writestr(name,data)
    data=buffer.getvalue()
    index={'format':'elsa-safe-playwright-trace-v1','traceVersion':8,'sourceSha':source_sha,'exporterSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'sha256':hashlib.sha256(data).hexdigest(),
        'counts':dict(exporter.counts),'omissions':['authentication screenshots','request and dynamic response bodies','headers and cookies','API results','scripts and source files','console arguments','runner trace'],
        'viewerInspection':'required before publication'}
    destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
    destination.with_suffix('.index.json').write_text(json.dumps(index,indent=2)+'\n')
    return index


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('destination',type=Path);parser.add_argument('--source-sha',required=True)
    args=parser.parse_args()
    try:index=export_trace(args.source,args.destination,args.source_sha)
    except (UnsafeTrace,zipfile.BadZipFile):parser.exit(1,'Safe trace export rejected the archive; raw input remains private.\n')
    print(json.dumps(index,indent=2))

if __name__=='__main__':main()
