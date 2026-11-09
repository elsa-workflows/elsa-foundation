"""Real Chromium controls for the T015 lifecycle mode preselection, not native Studio qualification."""
import json
from pathlib import Path
import subprocess
import unittest


def between(source, start, end):
    first = source.index(start)
    return source[first:source.index(end, first)]


class BrowserLifecycleSelectionTests(unittest.TestCase):
    def run_control(self, scenario, case="visibility-review-control"):
        tool = Path(__file__).resolve().parent
        source = (tool / "browser.mjs").read_text()
        # The actual driver's lifecycle helpers and the acknowledgement policy shape they rely on.
        helpers = (between(source, "const AUTONOMY_LABELS", "class AutonomyFailure") + between(source, "function canonical(", "async function requireAcknowledgedSelector(")
                   + between(source, "async function lifecycleBegin(", "async function resource("))
        journeys = between(source, "const LIFECYCLE_JOURNEYS", "const lifecycle =")
        script = r'''
import {createServer} from "node:http";
import {createRequire} from "node:module";
const require=createRequire(new URL("../../../Studio/package.json",import.meta.url));
const {chromium,expect:baseExpect}=require("@playwright/test");
const expect=baseExpect.configure({timeout:1500});
const SCENARIO=__SCENARIO__, CASE=__CASE__;
const html=`<!doctype html><button type="button" id="launch">Weaver</button><section role="region" aria-label="Weaver assistant" hidden>
<div class="weaver-auto-apply" data-autonomy-state="acknowledged"><button type="button" aria-label="Auto-apply mode" aria-busy="false">Manual review</button>
<div role="menu" aria-label="Auto-apply mode" hidden><button type="button" role="menuitemradio">Autopilot <small>Apply eligible changes</small></button></div></div>
<button type="button" id="send">Send</button></section><script>
const scenario=${JSON.stringify(SCENARIO)};let mode='manual';
const region=document.querySelector('section'),wrapper=document.querySelector('.weaver-auto-apply'),button=wrapper.querySelector('button'),menu=wrapper.querySelector('[role=menu]');
document.querySelector('#launch').addEventListener('click',()=>{region.hidden=false;});
button.addEventListener('click',()=>{menu.hidden=!menu.hidden;});
menu.querySelector('button').addEventListener('click',()=>{menu.hidden=true;if(scenario==='ignored')return;mode='full-auto';button.textContent='Autopilot';wrapper.dataset.autonomyState='requested';});
document.querySelector('#send').addEventListener('click',()=>fetch('/_elsa/agent/sessions',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({autonomyMode:mode})}));
</script>`;
const server=createServer((request,response)=>{
 if(request.method==='GET'&&request.url==='/resource'){response.setHeader('Content-Type','text/html');response.end(html);return;}
 if(request.method==='POST'&&request.url==='/_elsa/agent/sessions'){let body='';request.on('data',c=>body+=c);request.on('end',()=>{
  const requested=JSON.parse(body).autonomyMode;
  response.setHeader('Content-Type','application/json');
  response.end(JSON.stringify({data:{sessionId:'s1',policy:{autonomyMode:SCENARIO==='clamped'?'manual':requested,maxAutonomyMode:'full-auto',
   allowedAutonomyModes:['manual','auto-read-only','full-auto'],revision:0}},error:null}));});return;}
 response.statusCode=404;response.end();
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const backend='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const context=await browser.newContext(),page=await context.newPage();
const requests=SCENARIO==='early'?[{url:backend+'/_elsa/agent/sessions',method:'POST'}]:[],retained=[],result={evidence:[]};
const dock=()=>page.getByRole('region',{name:'Weaver assistant',exact:true});
const need=(condition,message)=>{if(!condition)throw new Error(message);};
const responseFor=suffix=>page.waitForResponse(response=>response.url().endsWith(suffix)&&response.request().method()==='POST',{timeout:5000});
const accepted=async(promise,name)=>{const response=await promise;const value=await response.json();retained.push(name);return {response,value};};
const owner=async(name,endpoint)=>{retained.push(name);return {value:{data:{sessionId:SCENARIO==='other-session'?'s2':endpoint.split('/').pop()}},receipt:{path:name}};};
const lifecycle=CASE;
const run=new (Object.getPrototypeOf(async function(){}).constructor)('page','dock','expect','need','requests','result','responseFor','accepted','owner','lifecycle',
 __JOURNEYS__+__HELPERS__+'\nconst created=await lifecycleBegin();await page.locator("#send").click();await lifecycleCreated(created);await lifecycleFinish();return result.lifecycle;');
let observed;
try {
 await page.goto(backend+'/resource');
 const outcome=await run(page,dock,expect,need,requests,result,responseFor,accepted,owner,lifecycle).then(value=>({value}),error=>({error}));
 observed={scenario:SCENARIO,lifecycle:outcome.value??null,failure:outcome.error?.message??null,retained};
} finally {
 await context.close();await browser.close();
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
console.log(JSON.stringify(observed));
'''.replace("__SCENARIO__", json.dumps(scenario)).replace("__CASE__", json.dumps(case)).replace("__HELPERS__", json.dumps(helpers)).replace("__JOURNEYS__", json.dumps(journeys))
        result = subprocess.run(["node", "--input-type=module", "-e", script], cwd=tool, capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def test_full_auto_is_selected_before_the_conversation_and_acknowledged(self):
        observed = self.run_control("valid")
        self.assertIsNone(observed["failure"])
        self.assertEqual({"caseKey": "visibility-review-control", "sessionId": "s1", "requestedMode": "full-auto", "sessionAfter": {"path": "lifecycle-session-after"},
                          "createdPolicy": {"autonomyMode": "full-auto", "maxAutonomyMode": "full-auto",
                                            "allowedAutonomyModes": ["manual", "auto-read-only", "full-auto"], "revision": 0}}, observed["lifecycle"])
        self.assertEqual(["autonomy-create-session", "lifecycle-session-after"], observed["retained"])

    def test_interruption_keeps_the_default_mode_without_selection(self):
        observed = self.run_control("valid", "interrupted-after-effect")
        self.assertIsNone(observed["failure"])
        self.assertEqual(("manual", None), (observed["lifecycle"]["createdPolicy"]["autonomyMode"], observed["lifecycle"]["requestedMode"]))

    def test_unacknowledged_late_ignored_or_foreign_selection_fails(self):
        for scenario, message in (("clamped", "did not acknowledge its selected mode"), ("early", "before the first native request"),
                                  ("ignored", "Autopilot"), ("other-session", "conversation changed")):
            with self.subTest(scenario=scenario):
                observed = self.run_control(scenario)
                self.assertIsNone(observed["lifecycle"])
                self.assertIn(message, observed["failure"])


if __name__ == "__main__":
    unittest.main()
