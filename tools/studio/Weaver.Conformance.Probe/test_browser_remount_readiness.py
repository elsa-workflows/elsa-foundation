"""Chromium controls for the driver readiness race, not native Studio qualification."""
import json
from pathlib import Path
import subprocess
import unittest


class BrowserRemountReadinessTests(unittest.TestCase):
    def run_control(self, scenario, *, old_driver=False, navigation="left"):
        tool = Path(__file__).resolve().parent
        source = (tool / "browser.mjs").read_text()
        start = source.index('  await expect(dock()).toHaveCount(0);', source.index('async function consumedIntentRemount'))
        end = source.index('  await page.getByLabel("Search Studio", { exact: true }).fill("");', start)
        actions = source[start:end]
        if old_driver:
            actions = actions.replace('  await expect(page.getByRole("button", { name: "Weaver", exact: true })).toBeEnabled();\n', '')
        script = r'''
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {createRequire} from 'node:module';
const require=createRequire(new URL('../../../Studio/package.json',import.meta.url));
const {chromium,expect:baseExpect}=require('@playwright/test');
const expect=baseExpect.configure({timeout:1500});
const scenario=SCENARIO,oldDriver=OLD_DRIVER,navigation=NAVIGATION;
let connect,connected,featuresRequests=0;
const connectionSeen=new Promise(resolve=>connected=resolve);
const html=`<!doctype html><input type="search" aria-label="Search Studio"><script>
fetch('/connection').then(()=>{
 document.body.innerHTML='<button>Weaver</button><input type="search" aria-label="Search Studio"><nav aria-label="${navigation === "top" ? "Search results" : "Settings"}"><a href="/settings">Settings</a></nav>';
 document.querySelector('input').addEventListener('input',event=>{
  const nav=document.querySelector('nav');
  nav.innerHTML='<a href="/settings">Settings</a>';
  if(event.target.value==='Features'&&${JSON.stringify(scenario)}!=='missing-features')nav.insertAdjacentHTML('beforeend','<a href="/features?backend=owned">Features</a>');
 });
});
</script>`;
const server=createServer((request,response)=>{
 if(request.url==='/connection'){connect=response;connected();return;}
 if(request.url==='/features?backend=owned'){featuresRequests++;response.end('Features');return;}
 response.setHeader('Content-Type','text/html');response.end(html);
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const studio='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const realPage=await browser.newPage();realPage.setDefaultTimeout(1500);
let released=false;
const release=()=>{if(!released&&scenario!=='never-ready'){released=true;connect.end('{}');}};
// Hold the actual connection response until the driver either observes the ready
// launcher or fills the temporary input. In the latter case, complete replacement
// before its next action, deterministically reproducing the captured lost query.
const interactions={
 getByRole:(role,options)=>{
  if(role==='button'&&options.name==='Weaver')release();
  return realPage.getByRole(role,options);
 },
 getByLabel:(label,options)=>{
  const locator=realPage.getByLabel(label,options);
  return {fill:async value=>{
   await locator.fill(value);
   if(!released){release();await expect(realPage.getByRole('button',{name:'Weaver',exact:true})).toBeVisible();}
  }};
 }
};
const page=new Proxy(realPage,{get:(target,key)=>key in interactions?interactions[key]
 :typeof target[key]==='function'?target[key].bind(target):target[key]});
const dock=()=>realPage.getByRole('region',{name:'Weaver assistant',exact:true});
const studioRoute=route=>studio+route+'?backend=owned';
const run=new (Object.getPrototypeOf(async function(){}).constructor)('page','dock','expect','studioRoute',ACTIONS);
try {
 await realPage.goto(studio+'/remount');await connectionSeen;
 const action=run(page,dock,expect,studioRoute);action.catch(()=>{});
 if(oldDriver||scenario!=='ready'){
  await assert.rejects(action);
  assert.equal(featuresRequests,0,'A missing ready shell or Features link must still fail.');
 }else{
  await action;assert.equal(featuresRequests,1);
  assert.equal(realPage.url(),studioRoute('/features'));
 }
 console.log(JSON.stringify({status:'passed',scenario,oldDriver,nativeStudioQualification:false}));
}finally{
 connect?.end();await browser.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
'''.replace("SCENARIO", json.dumps(scenario)).replace("OLD_DRIVER", json.dumps(old_driver)).replace("NAVIGATION", json.dumps(navigation)).replace("ACTIONS", json.dumps(actions))
        result = subprocess.run(["node", "--input-type=module", "-e", script], cwd=tool,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["status"], "passed")

    def test_replacement_shell_is_ready_before_filtering(self):
        for navigation in ("left", "top"):
            with self.subTest(navigation=navigation):
                self.run_control("ready", navigation=navigation)

    def test_old_driver_loses_filter_in_replacement_shell(self):
        for navigation in ("left", "top"):
            with self.subTest(navigation=navigation):
                self.run_control("ready", old_driver=True, navigation=navigation)

    def test_absent_ready_shell_and_absent_features_still_fail(self):
        for navigation in ("left", "top"):
            for scenario in ("never-ready", "missing-features"):
                with self.subTest(scenario=scenario, navigation=navigation):
                    self.run_control(scenario, navigation=navigation)


if __name__ == "__main__":
    unittest.main()
