"""Real Chromium control for the size review locator, not native Studio qualification."""
import json
from pathlib import Path
import subprocess
import unittest


def between(source, start, end):
    first = source.index(start)
    return source[first:source.index(end, first)]


class BrowserSizeReviewTests(unittest.TestCase):
    def run_control(self, outcome, requested=None):
        tool = Path(__file__).resolve().parent
        source = (tool / "browser.mjs").read_text()
        # Run the actual driver's decision/approval/application actions and the
        # helpers they call. The page keeps one card and changes its status in
        # place after each real HTTP response, as Studio's keyed card does.
        actions = between(source, '  const pending = dock()', '  const after = await owner("size-owner-after"')
        helpers = (between(source, "function responseFor(", "async function interceptAcknowledgement(")
                   + between(source, "async function approve(", "async function sendTool(")
                   + between(source, "class AutonomyFailure", "function canonical("))
        script = r'''
import assert from "node:assert/strict";
import {createServer} from "node:http";
import {createRequire} from "node:module";
const require=createRequire(new URL("../../../Studio/package.json",import.meta.url));
const {chromium,expect:baseExpect}=require("@playwright/test");
const expect=baseExpect.configure({timeout:1500});
const OUTCOME=OUTCOME_VALUE,REQUESTED=REQUESTED_VALUE;
const card=OUTCOME==="review"
 ? `<article class="agent-proposal" data-status="awaiting-approval"><div data-testid="acme-home-size-result">Preview: medium</div>
    <footer><button>Approve</button><button disabled>Apply</button></footer></article>`
 : `<article class="agent-proposal" data-status="executed"><div data-testid="acme-home-size-result">Acme independently confirmed the saved size</div></article>`;
const html=`<!doctype html><section role="region" aria-label="Weaver assistant" aria-busy="false">${card}</section><script>
const article=document.querySelector('.agent-proposal'),buttons=[...document.querySelectorAll('button')];
async function decide(path,status,text){
 const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});
 if(!response.ok)return;
 article.setAttribute('data-status',status);
 if(text)article.querySelector('[data-testid="acme-home-size-result"]').textContent=text;
 if(status==='approved'){buttons[0].disabled=true;buttons[1].disabled=false;}
}
buttons[0]?.addEventListener('click',()=>decide('/_elsa/agent/proposals/p1/approve','approved'));
buttons[1]?.addEventListener('click',()=>decide('/_elsa/agent/proposals/p1/execute','executed','Acme independently confirmed the saved size'));
</script>`;
const posts=[];
const server=createServer((request,response)=>{
 if(request.method==='POST'){posts.push(request.url);response.setHeader('Content-Type','application/json');response.end('{"data":{"status":"ok"}}');}
 else if(request.url==='/resource'){response.setHeader('Content-Type','text/html');response.end(html);}
 else {response.statusCode=404;response.end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const backend='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const context=await browser.newContext(),page=await context.newPage();
const dock=()=>page.getByRole('region',{name:'Weaver assistant',exact:true});
const need=(condition,message)=>{if(!condition)throw new Error(message);};
const diagnosticUrl=value=>value,retain=()=>({}),result={evidence:[]};
const before={value:{size:"small",revision:"r1"}},layoutBefore={value:{widgets:[]}};
const owner=async name=>name.startsWith("size-layout")?layoutBefore:(OUTCOME==="review"?before:{value:{size:"medium",revision:"r2"}});
const witness={sizeOwnerEndpoint:"/size",hostId:"host"},targetSize="medium";
const requestedMode=REQUESTED,initialMode=requestedMode;
const run=new (Object.getPrototypeOf(async function(){}).constructor)(
 'page','dock','expect','need','retain','result','backend','diagnosticUrl','owner','witness','before','layoutBefore','targetSize','initialMode','requestedMode',
 HELPERS+ACTIONS+'\nreturn {outcome,mismatch};');
let observed;
try {
 await page.goto(backend+'/resource');
 const expected=requestedMode==="manual"?"review":"automatic";
 if(expected!==OUTCOME) {
  // The acknowledged mode is authoritative: a different outcome fails precisely and never decides the card.
  const failure=await run(page,dock,expect,need,retain,result,backend,diagnosticUrl,owner,witness,before,layoutBefore,targetSize,initialMode,requestedMode).then(()=>null,error=>error);
  assert.equal(failure?.autonomyCause,'autonomy.mode_mismatch');
  assert.deepEqual(posts,[]);
  observed={status:'failed',outcome:OUTCOME,cause:failure.autonomyCause,posts,finalStatus:await page.locator('.agent-proposal').getAttribute('data-status'),nativeStudioQualification:false};
 } else {
  const decision=await run(page,dock,expect,need,retain,result,backend,diagnosticUrl,owner,witness,before,layoutBefore,targetSize,initialMode,requestedMode);
  assert.equal(decision.outcome,OUTCOME);
  assert.equal(decision.mismatch,false);
  const status=await page.locator('.agent-proposal').getAttribute('data-status');
  assert.equal(status,'executed');
  assert.deepEqual(posts,OUTCOME==="review"?['/_elsa/agent/proposals/p1/approve','/_elsa/agent/proposals/p1/execute']:[]);
  observed={status:'passed',outcome:decision.outcome,posts,finalStatus:status,nativeStudioQualification:false,browserVersion:browser.version()};
 }
} finally {
 await context.close();await browser.close();
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
console.log(JSON.stringify(observed));
'''.replace("OUTCOME_VALUE", json.dumps(outcome)).replace("REQUESTED_VALUE", json.dumps(requested or ("manual" if outcome == "review" else "full-auto"))).replace("HELPERS", json.dumps(helpers)).replace("ACTIONS", json.dumps(actions))
        result = subprocess.run(["node", "--input-type=module", "-e", script], cwd=tool,
                                capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        observed = json.loads(result.stdout)
        self.assertEqual(observed["outcome"], outcome)
        return observed

    def test_reviewed_size_card_is_kept_through_approval_and_application(self):
        self.run_control("review")

    def test_automatic_size_card_is_not_classified_as_review(self):
        self.run_control("automatic")

    def test_outcome_contradicting_the_acknowledged_mode_fails_without_deciding(self):
        for outcome, requested, status in (("automatic", "manual", "executed"), ("review", "full-auto", "awaiting-approval")):
            with self.subTest(requested=requested):
                observed = self.run_control(outcome, requested)
                self.assertEqual(("failed", "autonomy.mode_mismatch", []), (observed["status"], observed["cause"], observed["posts"]))
                self.assertEqual(status, observed["finalStatus"])


if __name__ == "__main__":
    unittest.main()
