from pathlib import Path
import urllib.request,urllib.parse,urllib.error,http.cookiejar,json
from html.parser import HTMLParser
r=Path('/tmp/elsa-2898-auth-pass-1');ready=json.loads((r/'diagnostic-original/ready.json').read_text());backend=ready['backendUrl'];studio=ready['studioUrl']
class Fields(HTMLParser):
 def __init__(self):super().__init__();self.fields={}
 def handle_starttag(self,tag,attrs):
  a=dict(attrs)
  if tag=='input' and a.get('name') and a.get('type')=='hidden':self.fields[a['name']]=a.get('value','')
redirects=[]
class Redirects(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,req,fp,code,msg,headers,newurl):redirects.append({'status':code,'destination':newurl});return super().redirect_request(req,fp,code,msg,headers,newurl)
jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar),Redirects())
url=backend+'/_elsa/identity/login?'+urllib.parse.urlencode({'returnUrl':studio+'/workflows/definitions?definition=14iBsLEuiJS&authProviderId=aspnetcore-identity'})
with opener.open(url,timeout=20) as response:parser=Fields();parser.feed(response.read().decode())
parser.fields.update(username='admin',password='Password123!')
request=urllib.request.Request(backend+'/_elsa/identity/login',data=urllib.parse.urlencode(parser.fields).encode(),headers={'Content-Type':'application/x-www-form-urlencoded','Origin':backend})
with opener.open(request,timeout=20) as response:landing={'status':response.status,'url':response.url};response.read()
results=[]
for name,path,origin in [('session','/_elsa/identity/session',None),('crossOriginSession','/_elsa/identity/session',studio),('crossOriginToken','/_elsa/identity/token',studio)]:
 headers={'Accept':'application/json'}
 if origin:headers['Origin']=origin
 try:
  with opener.open(urllib.request.Request(backend+path,headers=headers),timeout=20) as response:
   raw=response.read();payload=json.loads(raw);results.append({'name':name,'status':response.status,'sessionStatus':payload.get('status'),'hasAccessToken':bool(payload.get('accessToken')),'allowOrigin':response.headers.get('Access-Control-Allow-Origin'),'allowCredentials':response.headers.get('Access-Control-Allow-Credentials')})
 except urllib.error.HTTPError as e:results.append({'name':name,'status':e.code})
report={'sourceRevision':ready['sourceRevision'],'backendUrl':backend,'studioUrl':studio,'formFlow':'Supported password form with issued antiforgery input; values are never recorded','redirects':redirects,'landing':landing,'cookies':[{'name':c.name,'domain':c.domain,'path':c.path,'secure':c.secure,'valueLength':len(c.value)} for c in jar],'requests':results}
(r/'supported-login-probe.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
