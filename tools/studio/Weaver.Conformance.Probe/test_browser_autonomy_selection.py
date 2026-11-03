"""Real Chromium controls for the established-session mode change, not native Studio qualification."""
import json
from pathlib import Path
import subprocess
import unittest


def between(source, start, end):
    first = source.index(start)
    return source[first:source.index(end, first)]


class BrowserAutonomySelectionTests(unittest.TestCase):
    def run_control(self, scenario):
        tool = Path(__file__).resolve().parent
        source = (tool / "browser.mjs").read_text()
        # Run the actual driver's acknowledgement helpers against a small page whose
        # selector behaves like Studio's: it shows a pending change, sends a real PUT
        # and settles on the server's answer.
        helpers = between(source, "const AUTONOMY_LABELS", "async function readTurnSettled(")
        script = r'''
import assert from "node:assert/strict";
import {createServer} from "node:http";
import {createRequire} from "node:module";
const require=createRequire(new URL("../../../Studio/package.json",import.meta.url));
const {chromium,expect:baseExpect}=require("@playwright/test");
const expect=baseExpect.configure({timeout:1500});
const SCENARIO=SCENARIO_VALUE;
const created={autonomyMode:"full-auto",maxAutonomyMode:"full-auto",allowedAutonomyModes:["manual","auto-read-only","full-auto"],revision:0};
const html=`<!doctype html><section role="region" aria-label="Weaver assistant">
<div class="weaver-auto-apply" data-autonomy-state="acknowledged">
 <button type="button" aria-label="Auto-apply mode" aria-busy="false">Autopilot</button>
 <div role="menu" aria-label="Auto-apply mode" hidden><button type="button" role="menuitemradio" aria-checked="false">Manual review <small>Review every change</small></button></div>
</div></section><script>
const scenario=${JSON.stringify(SCENARIO)};
const wrapper=document.querySelector('.weaver-auto-apply'),button=wrapper.querySelector('button'),menu=wrapper.querySelector('[role=menu]');
button.addEventListener('click',()=>{menu.hidden=!menu.hidden;});
async function put(body){return fetch('/_elsa/agent/sessions/s1/autonomy',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});}
menu.querySelector('button').addEventListener('click',async()=>{
 menu.hidden=true;
 if(scenario==='no-put')return;
 wrapper.dataset.autonomyState='pending';wrapper.dataset.pendingMode='manual';button.setAttribute('aria-busy','true');
 const response=await put({autonomyMode:'manual',expectedRevision:scenario==='wrong-body'?7:0});
 if(scenario==='two-puts')await put({autonomyMode:'manual',expectedRevision:0});
 const value=await response.json();
 if(scenario==='stays-pending')return;
 wrapper.dataset.autonomyState='acknowledged';delete wrapper.dataset.pendingMode;button.setAttribute('aria-busy','false');
 if(response.ok&&value.data)button.textContent=value.data.autonomyMode==='manual'?'Manual review':'Autopilot';
 if(scenario==='status'||!response.ok){const status=document.createElement('div');status.setAttribute('role','status');status.setAttribute('aria-label','Auto-apply mode status');status.textContent='Not changed';document.querySelector('section').append(status);}
});
</script>`;
let held;
const server=createServer((request,response)=>{
 if(request.method==='GET'&&request.url==='/resource'){response.setHeader('Content-Type','text/html');response.end(html);return;}
 if(request.method==='PUT'&&request.url==='/_elsa/agent/sessions/s1/autonomy'){
  if(SCENARIO==='lost'){held=response;return;}
  response.setHeader('Content-Type','application/json');
  if(SCENARIO==='busy'){response.statusCode=409;response.end(JSON.stringify({data:null,error:{code:'agent.session.busy'}}));return;}
  response.end(JSON.stringify({data:{...created,autonomyMode:'manual',revision:SCENARIO==='wrong-ack'?5:1},error:null}));return;
 }
 response.statusCode=404;response.end();
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const backend='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const context=await browser.newContext(),realPage=await context.newPage();
const requests=[],retained=[],result={evidence:[]};
realPage.on('request',request=>{
 const url=new URL(request.url());
 if(url.origin!==backend||!url.pathname.startsWith('/_elsa/agent/')||!['POST','PUT','PATCH','DELETE'].includes(request.method()))return;
 requests.push({url:request.url(),method:request.method(),body:request.postDataJSON()});
});
const page={waitForResponse:(predicate,options)=>realPage.waitForResponse(predicate,{...options,timeout:1500})};
const dock=()=>realPage.getByRole('region',{name:'Weaver assistant',exact:true});
const diagnosticUrl=value=>value,retain=(name,value)=>{retained.push({name,value});return {path:name};};
const run=new (Object.getPrototypeOf(async function(){}).constructor)('page','dock','expect','backend','requests','result','retain','diagnosticUrl','created',
 HELPERS+'\nreturn changeEstablishedMode("s1","manual",created);');
let observed;
try {
 await realPage.goto(backend+'/resource');
 const outcome=await run(page,dock,expect,backend,requests,result,retain,diagnosticUrl,created).then(value=>({value}),error=>({error}));
 observed={scenario:SCENARIO,cause:outcome.error?.autonomyCause??null,failure:outcome.error&&!outcome.error.autonomyCause?outcome.error.message:null,
  acknowledgement:outcome.value?.acknowledgement??null,selector:outcome.value?.selector??null,
  requests:requests.map(item=>({method:item.method,path:new URL(item.url).pathname,body:item.body})),retained:retained.map(item=>item.name),
  update:retained.find(item=>item.name==='autonomy-mode-update')?.value??null,nativeStudioQualification:false};
} finally {
 held?.end();
 await context.close();await browser.close();
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
console.log(JSON.stringify(observed));
'''.replace("SCENARIO_VALUE", json.dumps(scenario)).replace("HELPERS", json.dumps(helpers))
        result = subprocess.run(["node", "--input-type=module", "-e", script], cwd=tool,
                                capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        observed = json.loads(result.stdout)
        self.assertIsNone(observed["failure"])
        return observed

    def test_one_acknowledged_put_settles_the_selector_on_the_requested_mode(self):
        observed = self.run_control("valid")
        self.assertIsNone(observed["cause"])
        self.assertEqual({"autonomyMode": "manual", "maxAutonomyMode": "full-auto",
                          "allowedAutonomyModes": ["manual", "auto-read-only", "full-auto"], "revision": 1}, observed["acknowledgement"])
        self.assertEqual([{"method": "PUT", "path": "/_elsa/agent/sessions/s1/autonomy",
                           "body": {"autonomyMode": "manual", "expectedRevision": 0}}], observed["requests"])
        self.assertEqual({"state": "acknowledged", "pendingMode": None, "ariaBusy": "false", "label": "Manual review",
                          "statusCount": 0, "mode": "manual"}, observed["selector"])
        self.assertEqual(["autonomy-mode-update-http", "autonomy-mode-update", "autonomy-mode-selector"], observed["retained"])
        self.assertEqual((200, "PUT", {"autonomyMode": "manual", "expectedRevision": 0}),
                         (observed["update"]["status"], observed["update"]["method"], observed["update"]["request"]))

    def test_missing_lost_duplicate_wrong_refused_or_unsettled_change_fails_precisely(self):
        for scenario, cause in (("no-put", "autonomy.update_not_sent"), ("lost", "autonomy.update_lost"),
                                ("two-puts", "autonomy.update_duplicated"), ("wrong-body", "autonomy.update_body_mismatch"),
                                ("busy", "autonomy.update_refused"), ("wrong-ack", "autonomy.acknowledgement_mismatch"),
                                ("stays-pending", "autonomy.selector_unacknowledged"), ("status", "autonomy.selector_unacknowledged")):
            with self.subTest(scenario=scenario):
                observed = self.run_control(scenario)
                self.assertEqual(cause, observed["cause"])
                self.assertIsNone(observed["acknowledgement"])
                self.assertNotIn("autonomy-mode-selector", observed["retained"])


if __name__ == "__main__":
    unittest.main()
