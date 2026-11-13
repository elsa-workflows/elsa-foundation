import base64
import copy
import importlib.util
import json
import tempfile
import struct
import unittest
import zipfile
from pathlib import Path

spec=importlib.util.spec_from_file_location('safe_trace',Path(__file__).with_name('export_safe_trace.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
CANARY='private-credential-canary-582b'
SHA='a'*40

def encoded(records):return b''.join(json.dumps(r).encode()+b'\n' for r in records)

def browser(context='owner'):
    return [
      {'type':'context-options','version':8,'origin':'library','browserName':'chromium','playwrightVersion':'1.61.1','contextId':context,'options':{'viewport':{'width':1280,'height':900}}},
      {'type':'before','callId':'one','startTime':1,'class':'Frame','method':'fill','pageId':'page','params':{'selector':'input[name="comparison"]','value':'0'}},
      {'type':'after','callId':'one','endTime':2,'afterSnapshot':'snap'},
      {'type':'frame-snapshot','snapshot':{'snapshotName':'snap','pageId':'page','frameId':'frame','frameUrl':'http://localhost:5000/questions','isMainFrame':True,'html':['HTML',{},['BODY',{},['INPUT',{'value':'0'}],['DIV',{},'No eligible denominator'],['DIV',{},'2026-11-13'],['DIV',{},'current.rate, baseline.rate, delta.rate']]],'viewport':{'width':1280,'height':900},'timestamp':2,'resourceOverrides':[]}},
    ]

def network(value):
    return {'type':'resource-snapshot','snapshot':{'request':{'method':'POST','url':'http://localhost:5000/api?token='+CANARY,'headers':[{'name':'Authorization','value':'Bearer '+CANARY}],'cookies':[{'name':'session','value':CANARY}],'postData':{'text':json.dumps(value)}},'response':{'status':200,'content':{'mimeType':'application/json','_sha1':'b'*40}},'timings':{},'time':1}}

class SafeTraceTests(unittest.TestCase):
    def export(self,records,extras=None):
        entries={'0-trace.trace':encoded(records),'1-trace.trace':encoded(browser('reader'))}
        entries.update(extras or {})
        exporter=m.Exporter(entries);result=exporter.export()
        all_text=b'\n'.join(result.values())
        self.assertNotIn(CANARY.encode(),all_text)
        return result,exporter

    def test_preserves_both_contexts_native_meaning_dates_and_action_order(self):
        result,exporter=self.export(browser())
        records=[json.loads(x) for x in result['0-trace.trace'].splitlines()]
        self.assertEqual(['context-options','before','after','frame-snapshot'],[r['type'] for r in records])
        self.assertEqual('0',records[1]['params']['value'])
        self.assertIn(b'No eligible denominator',result['0-trace.trace'])
        self.assertIn(b'2026-11-13',result['0-trace.trace'])
        self.assertEqual({'owner','reader'},exporter.contexts)

    def test_headers_cookies_token_response_and_url_are_omitted(self):
        record=network({'accessToken':CANARY,'nextCursor':CANARY})
        result,_=self.export(browser(),{'0-trace.network':encoded([record]),'resources/'+'b'*40:json.dumps({'accessToken':CANARY}).encode()})
        self.assertNotIn('resources/'+'b'*40,result)
        safe=json.loads(result['0-trace.network'])['snapshot']
        self.assertEqual('http://localhost:5000/api',safe['request']['url'])
        self.assertEqual([],safe['request']['headers'])
        self.assertNotIn('postData',safe['request'])
        self.assertNotIn('_sha1',safe['response']['content'])

    def test_api_fetch_encoded_params_and_base64_response_body_removed(self):
        records=browser()+[
          {'type':'before','callId':'fetch','startTime':3,'class':'APIRequestContext','method':'fetch','params':{'url':'http://localhost:5000/api#'+CANARY,'headers':{'Authorization':CANARY},'jsonData':{'password':CANARY},'encodedParams':CANARY}},
          {'type':'after','callId':'fetch','endTime':4,'result':{'binary':base64.b64encode(json.dumps({'token':CANARY}).encode()).decode()}},
        ]
        result,_=self.export(records)
        self.assertNotIn(b'encodedParams',result['0-trace.trace'])
        self.assertNotIn(b'binary',result['0-trace.trace'])

    def test_password_fills_titles_logs_snapshot_both_value_attributes_redacted(self):
        records=browser()
        records[1]['params']={'selector':'input[name="password"]','value':CANARY}
        records[1]['title']='Fill '+CANARY
        records[3]['snapshot']['html']=['HTML',{},['BODY',{},['INPUT',{'type':'password','value':CANARY,'__playwright_value_':CANARY}],CANARY]]
        records.append({'type':'log','callId':'one','time':1,'message':CANARY})
        result,_=self.export(records)
        self.assertIn(b'[redacted]',result['0-trace.trace'])

    def test_unreferenced_resources_console_script_source_and_runner_trace_removed(self):
        records=browser()+[
          {'type':'before','callId':'eval','startTime':3,'class':'Frame','method':'evaluateExpression','params':{'expression':CANARY,'arg':{'token':CANARY}}},
          {'type':'after','callId':'eval','endTime':4,'result':{'value':CANARY}},
          {'type':'console','time':4,'messageType':'log','text':CANARY,'args':[CANARY]},
        ]
        records[3]['snapshot']['html'].append(['SCRIPT',{'nonce':CANARY},CANARY])
        result,_=self.export(records,{'resources/orphan':CANARY.encode(),'test.trace':encoded([{'type':'context-options','version':8,'options':{'httpCredentials':{'password':CANARY}}}])})
        self.assertNotIn('test.trace',result)
        self.assertNotIn('resources/orphan',result)
        self.assertNotIn(b'expression',result['0-trace.trace'])
        self.assertIn(b'TEMPLATE',result['0-trace.trace'])

    def test_drill_tokens_parameter_map_and_jwt_are_redacted(self):
        records=browser();jwt='.'.join([base64.urlsafe_b64encode(json.dumps(part, separators=(',', ':')).encode()).decode().rstrip('=') for part in ({'alg': 'HS256'}, {'sub': 'synthetic-test-only'})] + ['x' * 43])
        records[3]['snapshot']['html'].extend([CANARY,jwt])
        result,_=self.export(records,{'resources/private.json':json.dumps({'drill':{'token':CANARY}}).encode()})
        self.assertNotIn(jwt.encode(),result['0-trace.trace'])

    def test_unknown_record_version_dom_shape_and_broken_references_rejected(self):
        variants=[]
        v=browser();v.append({'type':'unrecognized-record','secret':CANARY});variants.append(v)
        v=browser();v[0]['version']=9;variants.append(v)
        v=browser();v[1]['unknown']=CANARY;variants.append(v)
        v=browser();v[3]['snapshot']['html']=[{'unsupported':CANARY}];variants.append(v)
        for records in variants:
            with self.subTest(kind=variants.index(records)),self.assertRaises(m.UnsafeTrace):self.export(records)

    def test_unrecorded_source_snapshot_link_is_omitted_and_counted(self):
        records=browser();records[2]['afterSnapshot']='missing'
        result,exporter=self.export(records)
        self.assertNotIn(b'"afterSnapshot"',result['0-trace.trace'])
        self.assertEqual(1,exporter.counts['omittedUnrecordedSnapshotLinks'])

    def test_incremental_dom_backreferences_preserved(self):
        records=browser();records[3]['snapshot']['html'].append([[0,1]])
        result,_=self.export(records)
        self.assertIn(b'[[0,1]]',result['0-trace.trace'])

    def test_external_urls_and_active_attributes_removed(self):
        records=browser();records[3]['snapshot']['html'].append(['IFRAME',{'src':'https://external.invalid/?token='+CANARY,'srcdoc':CANARY,'onload':CANARY}])
        result,_=self.export(records)
        self.assertIn(b'about:blank',result['0-trace.trace'])
        self.assertNotIn(b'srcdoc',result['0-trace.trace'])
        self.assertNotIn(b'onload',result['0-trace.trace'])

    def test_bare_opaque_bearer_is_removed_without_any_token_named_field(self):
        records=browser();records[3]['snapshot']['html'].append(CANARY)
        extra={'0-trace.network':encoded([{'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/api','headers':[{'name':'Authorization','value':'Bearer '+CANARY}]},'response':{'status':200,'content':{}}}}])}
        self.export(records,extra)

    def test_ordinary_cookie_value_is_removed_without_any_token_named_field(self):
        records=browser();records[3]['snapshot']['html'].append(CANARY)
        extra={'0-trace.network':encoded([{'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/api','cookies':[{'name':'session','value':CANARY}]},'response':{'status':200,'content':{}}}}])}
        self.export(records,extra)

    def test_large_credential_response_is_inventoried(self):
        records=browser();records[3]['snapshot']['html'].append(CANARY)
        body=json.dumps({'padding':'x'*(3*1024*1024),'accessToken':CANARY}).encode()
        self.export(records,{'resources/private-response':body})

    def test_encoded_and_external_css_resources_are_omitted(self):
        digest='c'*40
        bad_css=[
            'body{background:url(data:text/plain;base64,'+base64.b64encode(CANARY.encode()).decode()+')}',
            'body{background:url(//external.invalid/image?credential='+CANARY+')}',
            'body{background:url(/image?credential='+CANARY+')}',
            'body{background:url(https://external.invalid/image)}',
            'body{background-image:image-set("https://external.invalid/image" 1x)}',
            'body{background:url(d\\61ta:text/plain;base64,abc)}',
        ]
        record={'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/style.css'},'response':{'status':200,'content':{'mimeType':'text/css','_sha1':digest}}}}
        for css in bad_css:
            with self.subTest(css=bad_css.index(css)):
                result,_=self.export(browser(),{'0-trace.network':encoded([record]),'resources/'+digest:css.encode()})
                self.assertNotIn('resources/'+digest,result)

    def test_srcset_formaction_and_style_nodes_do_not_retain_url_credentials(self):
        records=browser();records[3]['snapshot']['html'].extend([
          ['IMG',{'srcset':'//external.invalid/image?credential='+CANARY+' 1x'}],
          ['BUTTON',{'formaction':'http://localhost:5000/api?credential='+CANARY}],
          ['STYLE',{},'body{background:url(data:text/plain;base64,'+base64.b64encode(CANARY.encode()).decode()+')}'],
        ])
        result,_=self.export(records)
        self.assertNotIn(b'data:text',result['0-trace.trace'])
        self.assertNotIn(b'credential=',result['0-trace.trace'])

    def test_mime_spoofed_binary_rendering_resource_is_omitted(self):
        digest='c'*40
        record={'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/font.woff2'},'response':{'status':200,'content':{'mimeType':'font/woff2','_sha1':digest}}}}
        result,_=self.export(browser(),{'0-trace.network':encoded([record]),'resources/'+digest:b'\xffnot-a-font'})
        self.assertNotIn('resources/'+digest,result)

    def test_font_metadata_and_private_blocks_are_omitted(self):
        digest='c'*40
        record={'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/font.woff2'},'response':{'status':200,'content':{'mimeType':'font/woff2','_sha1':digest}}}}
        for signature,offset in [(b'wOFF',24),(b'wOFF',36),(b'wOF2',28),(b'wOF2',40)]:
            data=bytearray(100);data[:4]=signature;data[8:12]=struct.pack('>I',len(data));data[offset:offset+4]=struct.pack('>I',64)
            data[64:96]=base64.b64encode(CANARY.encode())[:32]
            result,_=self.export(browser(),{'0-trace.network':encoded([record]),'resources/'+digest:bytes(data)})
            self.assertNotIn('resources/'+digest,result)

    def test_binary_assets_are_scanned_for_encoded_bare_bearer(self):
        digest='c'*40;data=bytearray(200);data[:4]=b'wOF2';data[8:12]=struct.pack('>I',len(data))
        token=base64.b64encode(CANARY.encode());data[100:100+len(token)]=token
        record={'type':'resource-snapshot','snapshot':{'request':{'url':'http://localhost:5000/font.woff2','headers':[{'name':'Authorization','value':'Bearer '+CANARY}]},'response':{'status':200,'content':{'mimeType':'font/woff2','_sha1':digest}}}}
        result,_=self.export(browser(),{'0-trace.network':encoded([record]),'resources/'+digest:bytes(data)})
        self.assertNotIn('resources/'+digest,result)

    def test_valid_embedded_font_preserves_main_stylesheet(self):
        data=bytearray(100);data[:4]=b'wOF2';data[8:12]=struct.pack('>I',len(data))
        css='@font-face{src:url(data:font/woff2;base64,'+base64.b64encode(data).decode()+')}body{display:grid}'
        exporter=m.Exporter({'0-trace.trace':encoded(browser())})
        self.assertEqual(css,exporter.css(css))

    def test_archive_duplicate_traversal_and_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'bad.zip'
            for names in [['../outside'],['same','same'],['/absolute']]:
                with zipfile.ZipFile(path,'w') as archive:
                    for name in names:archive.writestr(name,b'private')
                with self.assertRaises(m.UnsafeTrace):m.read_archive(path)
            with zipfile.ZipFile(path,'w') as archive:
                info=zipfile.ZipInfo('link');info.external_attr=(0o120777)<<16;archive.writestr(info,'target')
            with self.assertRaises(m.UnsafeTrace):m.read_archive(path)

    def test_failed_export_never_writes_destination(self):
        with tempfile.TemporaryDirectory() as directory:
            source=Path(directory)/'source.zip';destination=Path(directory)/'safe.zip'
            with zipfile.ZipFile(source,'w') as archive:archive.writestr('0-trace.trace',encoded([{'type':'unexpected'}]))
            with self.assertRaises(m.UnsafeTrace):m.export_trace(source,destination,SHA)
            self.assertFalse(destination.exists())

if __name__=='__main__':unittest.main()
