"""Real Chromium controls for driver navigation, not native Studio qualification."""
import json
from pathlib import Path
import subprocess
import unittest


class BrowserCompletionTests(unittest.TestCase):
    def run_control(self, terminal):
        tool = Path(__file__).resolve().parent
        source = (tool / "browser.mjs").read_text()
        start = source.index('  await screenshot("resource-applied");')
        end = source.index('\n  if (next)', start)
        # Run the actual resource driver's last actions before its Home readback.
        # The small HTTP page supplies only a delayed token/finite SSE lifecycle.
        actions = source[start:end]
        script = r'''
import assert from "node:assert/strict";
import {createServer} from "node:http";
import {createRequire} from "node:module";
const require=createRequire(new URL("../../../Studio/package.json",import.meta.url));
const {chromium,expect:baseExpect}=require("@playwright/test");
const expect=baseExpect.configure({timeout:1200});
const deferred=()=>{let resolve;const promise=new Promise(value=>{resolve=value;});return {promise,resolve};};
const tokenSeen=deferred(),streamSeen=deferred(),requests=[];
let tokenResponse,streamResponse,navigationStarted=false,terminalAtNavigation;
const html=`<!doctype html><section role="region" aria-label="Weaver assistant" aria-busy="true"></section><script>
const controller=new AbortController();
addEventListener('pagehide',()=>controller.abort());
(async()=>{
 try {
  await fetch('/token',{signal:controller.signal});
  const response=await fetch('/stream',{signal:controller.signal});
  const text=await response.text();
  const event=JSON.parse(text.slice(6));
  window.observedTerminal=event.kind;
  if(['completed','turnCancelled'].includes(event.kind))document.querySelector('section').setAttribute('aria-busy','false');
 } catch(error) {window.observedFailure=error.name;}
})();
</script>`;
const server=createServer((request,response)=>{
 requests.push(request.url);
 if(request.url==='/resource'){response.setHeader('Content-Type','text/html');response.end(html);}
 else if(request.url==='/token'){tokenResponse=response;tokenSeen.resolve();}
 else if(request.url==='/stream'){streamResponse=response;streamSeen.resolve();}
 else if(request.url==='/dashboard'){response.setHeader('Content-Type','text/html');response.end('Home');}
 else {response.statusCode=404;response.end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const studio='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const context=await browser.newContext(),realPage=await context.newPage();
const page={goto:async url=>{
 navigationStarted=true;terminalAtNavigation=await realPage.evaluate(()=>window.observedTerminal);
 return realPage.goto(url);
}};
const dock=()=>realPage.getByRole('region',{name:'Weaver assistant',exact:true});
const screenshot=async()=>{};
const run=new (Object.getPrototypeOf(async function(){}).constructor)('page','studio','screenshot','expect','dock',ACTIONS);
let outcome;
try {
 await realPage.goto(studio+'/resource');
 await tokenSeen.promise;
 const action=run(page,studio,screenshot,expect,dock);
 action.catch(()=>{}); // Cleanup may abort an already-rejected old-driver action.
 // Observe after the driver's synchronous actions and queued microtasks. Token
 // and stream admission stay controlled by actual outstanding HTTP responses.
 await new Promise(resolve=>setImmediate(resolve));
 assert.equal(navigationStarted,false,'The driver navigated while ordinary token admission was pending.');
 await expect(dock()).toHaveAttribute('aria-busy','true');
 tokenResponse.end('{}');
 await streamSeen.promise;
 assert.equal(navigationStarted,false,'The driver navigated before an actual stream terminal.');
 await expect(dock()).toHaveAttribute('aria-busy','true');
 streamResponse.setHeader('Content-Type','text/event-stream');
 streamResponse.end('data: '+JSON.stringify({kind:TERMINAL})+'\n\n');
 if(TERMINAL==='not-terminal') {
  await assert.rejects(action,/aria-busy/);
  assert.equal(navigationStarted,false,'An unfinished turn must fail observation without navigating.');
  assert.equal(await realPage.evaluate(()=>window.observedTerminal),'not-terminal');
 } else {
  await action;
  assert.equal(navigationStarted,true);
  assert.equal(terminalAtNavigation,TERMINAL);
  assert.equal(new URL(realPage.url()).pathname,'/dashboard');
 }
 assert.equal(requests.filter(path=>path==='/token').length,1);
 assert.equal(requests.filter(path=>path==='/stream').length,1);
 assert.equal(requests.filter(path=>path==='/dashboard').length,TERMINAL==='not-terminal'?0:1);
 outcome={status:'passed',terminal:TERMINAL,tokenRequests:1,streamRequests:1,
  navigationAfterTerminal:TERMINAL!=='not-terminal',boundedIncompleteFailure:TERMINAL==='not-terminal',
  nativeStudioQualification:false,browserVersion:browser.version()};
} finally {
 tokenResponse?.end();streamResponse?.end();
 await context.close();await browser.close();
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
console.log(JSON.stringify(outcome));
'''.replace("ACTIONS", json.dumps(actions)).replace("TERMINAL", json.dumps(terminal))
        result = subprocess.run(["node", "--input-type=module", "-e", script], cwd=tool,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["terminal"], terminal)

    def test_navigation_waits_for_token_admission_and_actual_completed_or_cancelled_stream(self):
        for terminal in ("completed", "turnCancelled"):
            with self.subTest(terminal=terminal):
                self.run_control(terminal)

    def test_nonterminal_stream_fails_bounded_observation_without_navigation(self):
        self.run_control("not-terminal")


if __name__ == "__main__":
    unittest.main()
