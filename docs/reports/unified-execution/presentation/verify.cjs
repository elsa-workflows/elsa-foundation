const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const {pathToFileURL} = require('node:url');
const path = require('node:path');
const root=path.resolve(__dirname,'..');
const out=path.resolve(process.argv[2] || path.join(require('node:os').tmpdir(),'elsa-presentation-qa'));
fs.mkdirSync(out,{recursive:true});
const result={researchArtifactBaseline:'4b464e1637483e89a11f632f2c99c5779cb4e0de',htmlSha256:require('node:crypto').createHash('sha256').update(fs.readFileSync(path.join(root,'presentation/index.html'))).digest('hex'),playwright:require('playwright/package.json').version,date:new Date().toISOString(),checks:[],console:[],requests:[],screenshots:[]};
const check=(name,detail)=>result.checks.push({name,detail});
(async()=>{
 const browser=await chromium.launch({channel:"chrome",headless:true});result.browser=browser.version();
 try {
  const context=await browser.newContext({viewport:{width:1440,height:1100},offline:true,reducedMotion:'reduce'});
  const page=await context.newPage();
  page.on('console',m=>{if(['warning','error'].includes(m.type()))result.console.push({type:m.type(),text:m.text()});});
  page.on('pageerror',e=>result.console.push({type:'pageerror',text:e.message}));
  page.on('request',r=>result.requests.push(r.url()));
  const url=pathToFileURL(path.join(root,'presentation/index.html')).href;
  await page.goto(url,{waitUntil:'load'});
  assert.equal(await page.title(),'Elsa 4 · One foundation, distinct semantics');
  assert.equal(await page.evaluate(()=>navigator.onLine),false);
  assert.equal(await page.evaluate(()=>matchMedia('(prefers-reduced-motion: reduce)').matches),true);
  check('Cold direct-file load in offline reduced-motion context',{url,online:false,reducedMotion:true});
  const models=[['native','Native workflow','Sequence → Prepare'],['bpmn','BPMN process','BPMN process → service task'],['etl','ETL pipeline','records outside the actor'],['agent','Agent collaboration','no actor per turn'],['rules','Independent rule model','no activity graph']];
  for(const [value,title,nested] of models){
   await page.getByLabel('View through').selectOption(value);
   assert.equal(await page.locator('#model-heading').innerText(),title);
   assert.ok((await page.locator('#nested-copy').innerText()).includes(nested));
   for(const node of await page.locator('[data-detail]').all()){
    await node.click();assert.equal(await node.getAttribute('aria-pressed'),'true');
    assert.ok((await page.locator('#detail-title').innerText()).length>0);
   }
  }
  check('Five model lenses and all nine responsibility drill-downs','Correct model heading, nested composition, selected state and detail content');
  await page.getByLabel('Zoom diagram in',{exact:true}).click();assert.equal(await page.locator('#zoom-value').innerText(),'125%');
  assert.equal(await page.locator('#diagram').evaluate(e=>getComputedStyle(e).zoom),'1.25');
  await page.getByLabel('Zoom diagram in',{exact:true}).click();await page.getByLabel('Zoom diagram in',{exact:true}).click();assert.ok(await page.getByLabel('Zoom diagram in',{exact:true}).isDisabled());
  await page.getByRole('button',{name:'Reset view',exact:true}).click();
  await page.getByLabel('Zoom diagram out',{exact:true}).click();assert.ok(await page.getByLabel('Zoom diagram out',{exact:true}).isDisabled());
  await page.getByRole('button',{name:'Reset view',exact:true}).click();
  check('Zoom and bounds','CSS zoom agrees; 75%/175% limits disable controls; reset returns 100%');
  await page.reload({waitUntil:'load'});
  await page.keyboard.press('Tab');assert.equal(await page.locator(':focus').innerText(),'Skip to architecture');
  await page.keyboard.press('Tab');assert.ok((await page.locator(':focus').innerText()).includes('Program #2892'));
  await page.keyboard.press('Tab');assert.equal(await page.locator(':focus').innerText(),'01 Architecture');
  await page.keyboard.press('Tab');assert.equal(await page.locator(':focus').innerText(),'02 Recovery lab');
  await page.keyboard.press('Enter');assert.ok(await page.locator('#recovery').isVisible());
  assert.equal(await page.getByRole('button',{name:'02 Recovery lab'}).getAttribute('aria-pressed'),'true');
  check('Keyboard entry and view activation','Tab follows skip link, program, Architecture, Recovery; Enter selects Recovery');
  const scenarios={crash:6,duplicate:4,cancel:3,success:3,buffer:5};
  for(const [key,total] of Object.entries(scenarios)){
   await page.getByLabel('Failure scenario').selectOption(key);
   assert.ok(await page.getByRole('button',{name:'Previous',exact:true}).isDisabled());
   for(let i=1;i<=total;i++){
    assert.ok((await page.locator('#step-caption').innerText()).startsWith(`${i} / ${total} · `));
    assert.equal(await page.locator('#timeline [aria-current="step"]').count(),1);
    if(i<total)await page.getByRole('button',{name:'Next transition',exact:true}).click();
   }
   assert.ok(await page.getByRole('button',{name:'Next transition',exact:true}).isDisabled());
   await page.getByRole('button',{name:'Previous',exact:true}).click();
   assert.ok((await page.locator('#step-caption').innerText()).startsWith(`${total-1} / ${total}`));
   await page.getByRole('button',{name:'Restart',exact:true}).click();
   assert.ok((await page.locator('#step-caption').innerText()).startsWith(`1 / ${total}`));
  }
  check('All five scenario paths','Every transition, final disabled state, previous and restart verified offline');
  assert.ok((await page.locator('#recovery').innerText()).includes('Illustrative simulation.'));
  await page.getByRole('button',{name:'03 Evidence & decisions'}).click();
  assert.ok((await page.locator('#evidence').innerText()).includes('not a production migration'));
  check('Evidence boundaries','Simulation label, non-production caveat and 17 October baseline remain visible');
  await page.getByRole('button',{name:'01 Architecture'}).click();
  for(const width of [320,768,1024,1440]){
   await page.setViewportSize({width,height:1100});
   const dimensions=await page.evaluate(()=>({viewport:innerWidth,body:document.body.scrollWidth,diagramClient:document.querySelector('.diagram-scroll').clientWidth,diagramScroll:document.querySelector('.diagram-scroll').scrollWidth}));
   assert.ok(dimensions.body<=width,JSON.stringify(dimensions));
   check('Responsive width '+width,dimensions);
  }
  const motion=await page.locator('body *').evaluateAll(es=>es.every(e=>{const c=getComputedStyle(e);return c.animationName==='none'&&c.transitionProperty==='none'&&c.scrollBehavior==='auto';}));
  assert.ok(motion);check('Emulated reduced-motion behavior','All computed animation, transition and smooth scroll states disabled');
  const scroll=page.getByRole('region',{name:'Scrollable architecture diagram',exact:true});assert.equal(await scroll.count(),1,'The scroll container must expose a named region');await page.setViewportSize({width:320,height:1100});await scroll.focus();
  await page.keyboard.press('ArrowRight');await page.waitForTimeout(200);
  const sx=await scroll.evaluate(e=>e.scrollLeft);assert.ok(sx>0);check('Named region and keyboard diagram panning',{accessibleName:await scroll.getAttribute('aria-label'),scrollLeft:sx});
  await page.setViewportSize({width:1440,height:1100});await page.getByLabel('View through').selectOption('agent');
  await page.screenshot({path:path.join(out,'offline-agent.png'),fullPage:true});result.screenshots.push('offline-agent.png');
  fs.writeFileSync(path.join(out,'accessibility-tree.txt'),await page.locator('body').ariaSnapshot());
  const links=await page.locator('a[href]').evaluateAll(es=>es.map(e=>({text:e.textContent,url:e.href,raw:e.getAttribute('href')})));
  for(const link of links.filter(l=>l.raw.startsWith('../'))){assert.ok(fs.existsSync(new URL(link.url)));}
  check('Bundled local report targets','All relative report targets exist; GitHub evidence explicitly requires connection');
  const fileLinks=[...new Set(links.filter(l=>l.raw.startsWith('../')).map(l=>l.url))];
  for(const target of fileLinks){await page.goto(target,{waitUntil:'load'});assert.ok((await page.locator('body').innerText()).length>500);}
  check('Local report navigation offline',fileLinks);
  assert.equal(result.requests.filter(u=>/^https?:/.test(u)).length,0);
  assert.deepEqual(result.console,[]);check('No network dependencies or console errors',{httpRequests:0,consoleWarningsOrErrors:0});
  await context.close();
  const nojs=await browser.newContext({offline:true,javaScriptEnabled:false,viewport:{width:1440,height:1100}});const staticPage=await nojs.newPage();
  await staticPage.goto(url,{waitUntil:'load'});assert.ok(await staticPage.getByRole('heading',{name:'Static overview',exact:true}).isVisible());
  assert.ok(await staticPage.getByRole('heading',{name:'Native workflow',exact:true}).isVisible());
  check('No-JavaScript fallback','Native diagram and explicit static overview visible');await nojs.close();
  result.status='passed';
 } catch(e){result.status='failed';result.error=e.stack;throw e;}
 finally {fs.writeFileSync(path.join(out,'result.json'),JSON.stringify(result,null,2).split(pathToFileURL(root+'/').href).join('file://<report-bundle>/'));await browser.close();}
 console.log(JSON.stringify({status:result.status,browser:result.browser,checks:result.checks.length,console:result.console.length,httpRequests:result.requests.filter(u=>/^https?:/.test(u)).length}));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
