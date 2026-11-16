// Real UI acceptance for the owned direct-human fixture; no mocked network or domain-state writes.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { pathToFileURL } from 'node:url';
const repo=process.cwd();
const { chromium, expect }=await import(pathToFileURL(path.join(repo,'Studio/node_modules/@playwright/test/index.mjs')).href);
if (!process.argv[2]) throw new Error('Usage: node tools/bpmn/drive_direct_studio.mjs OUTPUT [INPUT_BPMN]');
const output=path.resolve(process.argv[2]);
const input=path.resolve(process.argv[3] ?? 'tools/bpmn/fixtures/direct-human.bpmn');
const ready=JSON.parse(fs.readFileSync(path.join(output,'ready.json'),'utf8'));
if(ready.family!=='direct-human')throw new Error('Expected the owned fresh direct-human fixture.');
const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
fs.writeFileSync(path.join(output,'browser-started.json'),JSON.stringify({sourceRevision:ready.sourceRevision,driverSha256:hash(fileURLToPath(import.meta.url)),inputSha256:hash(input)})+'\n',{flag:'wx'});
const events=[], actions=[], consoles=[];
const pendingReads=new Set();
const browser=await chromium.launch({headless:true});
const write=(name,value)=>fs.writeFileSync(path.join(output,name),JSON.stringify(value,null,2)+'\n');
const mark=(action)=>{actions.push({action,at:new Date().toISOString()});console.log(action);};
let currentPage;
function observe(page){
 currentPage=page;
 page.on('pageerror',error=>consoles.push({kind:'pageerror',message:error.message}));
 page.on('response',response=>{
  const request=response.request(),url=new URL(response.url());
  if(!['POST','PUT'].includes(request.method())||!(url.pathname.startsWith('/bpmn/')||url.pathname.includes('/interchange/bpmn/')||url.pathname.startsWith('/design/workflows/drafts/')))return;
  const read=(async()=>{let body;try{body=await response.json();}catch{body=null;}
   events.push({at:new Date().toISOString(),method:request.method(),path:url.pathname,status:response.status(),request:request.postDataJSON(),response:body});
  })(); pendingReads.add(read);read.finally(()=>pendingReads.delete(read));
 });
}
async function shot(page,name){
 await Promise.all([...pendingReads]);
 await page.screenshot({path:path.join(output,name+'.png'),fullPage:true});
 write(name+'.json',{url:page.url(),text:await page.locator('body').innerText(),mode:await page.locator('html').getAttribute('data-theme-mode'),viewport:page.viewportSize()});
}
async function login(page,url){
 await page.goto(url);
 const username=page.getByLabel('Username',{exact:true});
 const visible=await username.waitFor({state:'visible',timeout:5000}).then(()=>true,()=>false);
 if(visible){await username.fill('admin');await page.getByLabel('Password',{exact:true}).fill('Password123!');await page.getByRole('button',{name:'Sign in',exact:true}).click();}
 await page.waitForURL(value=>value.origin===ready.studioUrl,{timeout:30000});
 await expect(page.getByRole('main').first()).toBeVisible({timeout:30000});
}
async function command(action,label){
 const receipt=path.join(output,(action==='finish'?'process':label)+'.json');
 if(fs.existsSync(receipt))throw new Error('Refusing to overwrite an existing capture.');
 fs.writeFileSync(path.join(output,'command.json'),JSON.stringify({action,...(label?{label}:{})}));
 const end=Date.now()+180000;
 while(!fs.existsSync(receipt)){if(Date.now()>end)throw new Error('Owned controller did not acknowledge '+action);await new Promise(resolve=>setTimeout(resolve,200));}
 mark('Controller '+action+(label?' '+label:''));
}
async function exportFile(page,label,name){
 await page.getByRole('button',{name:'More workflow actions',exact:true}).click();
 const download=page.waitForEvent('download');await page.getByRole('menuitem',{name:label,exact:true}).click();
 await (await download).saveAs(path.join(output,name));
}
async function settleStart(page){
 await expect(async()=>{
  if(await page.getByRole('button',{name:'Open run',exact:true}).isVisible())return;
  const check=page.getByRole('button',{name:'Check outcome',exact:true});
  if(await check.isVisible() && await check.isEnabled({timeout:500}))await check.click({timeout:500});
  await expect(page.getByRole('button',{name:'Open run',exact:true})).toBeVisible({timeout:500});
 }).toPass({timeout:30000,intervals:[200,500]});
}
async function settleCompleted(page){
 await expect(async()=>{
  if((await page.locator('.wf-bpmn-run-heading').innerText()).includes('Completed'))return;
  const check=page.getByRole('button',{name:'Check outcome',exact:true});
  if(await check.isVisible() && await check.isEnabled({timeout:500}))await check.click({timeout:500});
  await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed',{timeout:500});
 }).toPass({timeout:30000,intervals:[200,500]});
}
try{
 let context=await browser.newContext({viewport:{width:1280,height:900}});
 let page=await context.newPage();observe(page);
 await login(page,ready.studioUrl+'/workflows/definitions');mark('Signed in through the real form');
 await page.getByRole('button',{name:'Create workflow',exact:true}).first().click();
 const dialog=page.getByRole('dialog',{name:/Create Workflow/i});
 await dialog.getByLabel('Display name',{exact:true}).fill('Direct approval journey');
 await dialog.getByText('BPMN',{exact:true}).click(); await expect(dialog.getByRole('radio',{name:'BPMN',exact:true})).toBeChecked();
 await dialog.getByRole('button',{name:'Create',exact:true}).click();
 await expect(page.locator('.wf-editor-toolbar')).toBeVisible({timeout:30000});mark('Created BPMN workflow through Studio');
 const definitionUrl=page.url();
 await page.getByLabel('Import BPMN file',{exact:true}).setInputFiles(input);
 await expect(page.locator('.react-flow__node[data-id="review"]')).toContainText('Review order',{timeout:30000});
 await page.locator('.react-flow__node[data-id="review"]').click();
 await page.getByLabel('Task execution',{exact:true}).selectOption('human');
 await expect(page.getByLabel('Task execution',{exact:true})).toHaveValue('human');
 await expect(page.locator('.react-flow__node[data-id="review"]')).toContainText('Human wait');mark('Selected Human wait in the inspector');
 await page.getByRole('button',{name:'Save',exact:true}).click();
 await expect(page.locator('.wf-editor-save-status')).toHaveText(/^(Saved|Autosaved)$/,{timeout:30000});mark('Imported and explicitly bound human work; saved');
 await exportFile(page,'Export BPMN','human-authored-export.bpmn');
 await exportFile(page,'Export JSON','human-authored-export.json');
 await page.getByLabel('Import BPMN file',{exact:true}).setInputFiles(path.join(output,'human-authored-export.bpmn'));
 await expect(page.locator('.react-flow__node[data-id="review"]')).toContainText('Review order',{timeout:30000});
 await page.locator('.react-flow__node[data-id="review"]').click();
 await expect(page.getByLabel('Task execution',{exact:true})).toHaveValue('human');
 await page.getByRole('button',{name:'Save',exact:true}).click();
 await expect(page.locator('.wf-editor-save-status')).toHaveText(/^(Saved|Autosaved)$/,{timeout:30000});
 await page.reload();await expect(page.locator('.wf-editor-toolbar')).toBeVisible({timeout:30000});
 await page.locator('.react-flow__node[data-id="review"]').click();
 await expect(page.getByLabel('Task execution',{exact:true})).toHaveValue('human');mark('Exported, reimported and reopened the saved human binding');
 await page.getByRole('button',{name:'Publish BPMN',exact:true}).click();
 await expect(page.getByRole('button',{name:'BPMN processes',exact:true})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'BPMN processes',exact:true}).click();
 await expect(page.getByRole('heading',{name:'BPMN processes',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Start process',exact:true})).toHaveCount(1);
 await page.getByRole('button',{name:'Start process',exact:true}).click();await settleStart(page);
 await page.getByRole('button',{name:'Open run',exact:true}).click();
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Waiting for a person',{timeout:30000});
 const firstRunUrl=page.url();await shot(page,'ui-human-wait');await command('checkpoint','human-wait');
 await context.close();await command('restart','restarted-wait');
 context=await browser.newContext({viewport:{width:1280,height:900}});page=await context.newPage();observe(page);
 await login(page,ready.studioUrl);await page.getByRole('link',{name:'BPMN runs',exact:true}).click();
 await expect(page.getByRole('button',{name:'Open run',exact:true})).toHaveCount(1);await page.getByRole('button',{name:'Open run',exact:true}).click();
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Waiting for a person',{timeout:30000});
 if(new URL(page.url()).pathname!==new URL(firstRunUrl).pathname)throw new Error('Fresh navigation found a different run.');
 await shot(page,'ui-recovered-wait');mark('Fresh browser discovered the same persisted wait through BPMN runs');
 const complete=page.getByRole('button',{name:'Complete work',exact:true});await expect(complete).toBeEnabled();
 let tabs=0;for(;tabs<100;tabs++){await page.keyboard.press('Tab');if(await complete.evaluate(element=>element===document.activeElement))break;}
 if(tabs===100)throw new Error('Human completion could not be reached by keyboard.');
 await page.keyboard.press('Enter');mark('Completed human work using Tab and Enter');
 await settleCompleted(page);
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed',{timeout:30000});
 await expect(page.getByText('No outstanding work.',{exact:true})).toBeVisible();await shot(page,'ui-completed');await command('checkpoint','completed');
 await command('restart','restarted-terminal');await page.reload();
 if(new URL(page.url()).origin!==ready.studioUrl)await login(page,page.url());
 await expect(page.getByRole('button',{name:'Replay last completion',exact:true})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'Replay last completion',exact:true}).click();
 await expect(page.getByText('Last completion: accepted (replayed).',{exact:true})).toBeVisible({timeout:30000});
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed');await shot(page,'ui-replayed');
 for(const mode of ['light','dark']){
  await page.setViewportSize({width:1280,height:900});await page.getByRole('radio',{name:mode==='light'?'Light':'Dark',exact:true}).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme-mode',mode);
  for(const width of [1280,320]){await page.setViewportSize({width,height:900});await shot(page,`ui-${mode}-${width}`);
   const size=await page.evaluate(()=>({width:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
   if(size.scroll>size.width+1)throw new Error(`Horizontal overflow at ${mode}/${width}: ${JSON.stringify(size)}`);
  }
 }
 await Promise.all([...pendingReads]);if(consoles.length)throw new Error('Browser page errors were recorded.');write('browser-receipt.json',{sourceRevision:ready.sourceRevision,definitionUrl,firstRunUrl,recoveredRunUrl:page.url(),keyboardTabs:tabs+1,actions,events,pageErrors:consoles,completed:true});
 mark('Real Studio journey complete; screenshots and HTTP receipts saved.');
 // Keep the owned pair available for read-only evidence inspection. The caller finishes the controller after verification.
}catch(error){
 if(currentPage){await shot(currentPage,'ui-failure').catch(()=>{});}
 await Promise.all([...pendingReads]);write('browser-failure.json',{sourceRevision:ready.sourceRevision,error:String(error),actions,events,pageErrors:consoles});
 console.error(error);process.exitCode=1;
}finally{await browser.close();}
