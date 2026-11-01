// Actual owned Studio journey: HTTP/file ETL, native service, held fault and exact retry after restart.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
const repo=process.cwd();
const { chromium, expect }=await import(pathToFileURL(path.join(repo,'Studio/node_modules/@playwright/test/index.mjs')).href);
if(!process.argv[2])throw new Error('Usage: node tools/bpmn/drive_direct_etl_studio.mjs OUTPUT');
const output=path.resolve(process.argv[2]),ready=JSON.parse(fs.readFileSync(path.join(output,'ready.json'),'utf8'));
if(ready.family!=='direct-etl')throw new Error('Expected the isolated direct-etl composition.');
const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const write=(name,value)=>fs.writeFileSync(path.join(output,name),JSON.stringify(value,null,2)+'\n');
fs.writeFileSync(path.join(output,'browser-started.json'),JSON.stringify({productSource:ready.sourceRevision,harnessSource:ready.harnessSourceRevision,driverSha256:hash(fileURLToPath(import.meta.url))})+'\n',{flag:'wx'});
const browser=await chromium.launch({headless:true}),events=[],actions=[],errors=[],pending=new Set();
let currentPage;
const mark=action=>{actions.push({action,at:new Date().toISOString()});console.log(action);};
function observe(page){currentPage=page;page.on('pageerror',e=>errors.push(e.message));page.on('response',response=>{
 const request=response.request(),url=new URL(response.url());
 if(!['POST','PUT'].includes(request.method())||!/^\/(bpmn|etl|interchange|design\/workflows|publishing\/workflows)\//.test(url.pathname))return;
 const read=(async()=>{let body;try{body=await response.json();}catch{body=null;}
 events.push({method:request.method(),path:url.pathname,status:response.status(),request:request.postDataJSON(),response:body});})();pending.add(read);read.finally(()=>pending.delete(read));
});}
async function shot(page,name){await Promise.all([...pending]);await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:path.join(output,name+'.png'),fullPage:true});write(name+'.json',{url:page.url(),text:await page.locator('body').innerText(),viewport:page.viewportSize()});write('browser-events.json',{actions,events,errors});}
async function login(page,url){await page.goto(url);const username=page.getByLabel('Username',{exact:true});
 const visible=await username.waitFor({state:'visible',timeout:5000}).then(()=>true,()=>false);
 if(visible){await username.fill('admin');await page.getByLabel('Password',{exact:true}).fill('Password123!');await page.getByRole('button',{name:'Sign in',exact:true}).click();}
 await page.waitForURL(value=>value.origin===ready.studioUrl,{timeout:30000});await expect(page.getByRole('main').first()).toBeVisible({timeout:30000});}
async function command(action,label){const receipt=path.join(output,(action==='finish'?'process':label)+'.json');
 if(fs.existsSync(receipt))throw new Error('Refusing to overwrite a checkpoint.');fs.writeFileSync(path.join(output,'command.json'),JSON.stringify({action,label}));await expect.poll(()=>fs.existsSync(receipt),{timeout:90000,intervals:[200,500]}).toBe(true);return JSON.parse(fs.readFileSync(receipt,'utf8'));}
async function binding(page,label,value){const select=page.getByLabel(label,{exact:true});await expect(select).toBeEnabled();await select.selectOption(value);await expect(select).toHaveValue(value);}
async function publishEtl(page){await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.getByRole('button',{name:'Validate saved revision',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Validate saved revision',exact:true}).click();await expect(page.getByRole('status').filter({hasText:/is valid as immutable version/})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'Publish immutable version',exact:true}).click();await page.getByRole('dialog',{name:'Publish immutable ETL version?'}).getByRole('button',{name:'Publish version',exact:true}).click();}
async function createWorkflow(page,name,kind){await page.goto(ready.studioUrl+'/workflows/definitions');await page.getByRole('button',{name:'Create workflow',exact:true}).first().click();
 const dialog=page.getByRole('dialog',{name:/Create Workflow/i});await dialog.getByLabel('Display name',{exact:true}).fill(name);await dialog.getByText(kind,{exact:true}).click();
 await dialog.getByRole('button',{name:'Create',exact:true}).click();await expect(page.locator('.wf-editor-toolbar')).toBeVisible({timeout:30000});}
async function saveWorkflow(page){await page.getByRole('button',{name:'Save',exact:true}).click();await expect(page.locator('.wf-editor-save-status')).toHaveText(/^(Saved|Autosaved)$/,{timeout:30000});}
async function held(page){await expect(async()=>{const refresh=page.getByRole('button',{name:'Refresh run',exact:true});if(await refresh.isEnabled())await refresh.click();
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Needs attention',{timeout:500});await expect(page.getByRole('button',{name:'Retry service',exact:true})).toBeEnabled({timeout:500});}).toPass({timeout:60000,intervals:[200,500]});}
try{
 let context=await browser.newContext({viewport:{width:1440,height:1000}}),page=await context.newPage();observe(page);await login(page,ready.studioUrl+'/etl/connectors');
 await page.getByRole('button',{name:'New definition',exact:true}).click();let dialog=page.getByRole('dialog',{name:'New ETL definition'});
 await dialog.getByRole('textbox',{name:'New ETL definition'}).fill('direct-service-orders');await dialog.getByRole('button',{name:'Create draft',exact:true}).click();
 await binding(page,'Source HTTP binding','http-source');await page.getByLabel('Records JSON Pointer',{exact:true}).fill('/items');await page.getByLabel('Continuation query parameter',{exact:true}).fill('pageToken');
 await page.getByLabel('Accepted records destination kind',{exact:true}).selectOption('jsonl');await binding(page,'Accepted records binding','output');
 await page.getByLabel('Rejected records destination kind',{exact:true}).selectOption('jsonl');await binding(page,'Rejected records binding','errors');
 await page.getByLabel('Maximum delivery attempts',{exact:true}).fill('3');await publishEtl(page);
 await page.getByRole('button',{name:/^View version 1\b/}).click();const selection=(await page.getByRole('region',{name:'Published ETL artifact'}).locator('code').first().innerText()).trim();
 write('pipeline-selection.json',{selection});await shot(page,'ui-etl-published-v1');mark('Published the two-shard HTTP to JSONL pipeline through Studio');
 await createWorkflow(page,'Import orders service','Sequence');await page.getByRole('button',{name:'Add the first step',exact:true}).press('Enter');
 const picker=page.getByRole('listbox',{name:'Activity picker'});await expect(picker).toBeVisible();const search=page.getByRole('searchbox',{name:'Search activities'});await search.fill('Run Etl');await search.press('Enter');await expect(picker).toBeHidden();
 await binding(page,'Pipeline catalog','connector');await binding(page,'Published ETL definition','direct-service-orders');await binding(page,'Published ETL version',selection);await saveWorkflow(page);
 await page.getByRole('button',{name:'Publish options',exact:true}).click();await page.getByRole('menuitemradio',{name:'Review & publish',exact:true}).click();
 dialog=page.getByRole('dialog',{name:'Publish workflow',exact:true});await expect(dialog.getByRole('button',{name:'Publish',exact:true})).toBeEnabled({timeout:30000});await dialog.getByRole('button',{name:'Publish',exact:true}).click();
 dialog=page.getByRole('dialog',{name:'Published',exact:true});await expect(dialog).toBeVisible({timeout:30000});await dialog.getByRole('button',{name:'Close',exact:true}).click();await shot(page,'ui-native-worker-published');mark('Created and published ordinary Sequence with a RunEtl child');
 await createWorkflow(page,'Direct ETL recovery','BPMN');await page.getByLabel('Import BPMN file',{exact:true}).setInputFiles(path.join(repo,'tools/bpmn/fixtures/direct-etl.bpmn'));
 await page.locator('.react-flow__node[data-id="import"]').click();await page.getByRole('button',{name:'Bind a published service workflow',exact:true}).click();
 const workflow=page.getByRole('combobox',{name:'Workflow',exact:true});await workflow.fill('Import orders service');await page.getByRole('listbox',{name:'Workflow search results'}).getByRole('option').filter({hasText:'Import orders service'}).click();
 const publications=page.getByLabel('Service publication',{exact:true});await expect.poll(()=>publications.locator('option').count()).toBeGreaterThan(1);await publications.selectOption({index:1});
 await page.getByLabel('Service maximum attempts',{exact:true}).fill('2');await page.getByLabel('Service business error code',{exact:true}).fill('etl-input-error');await page.getByRole('button',{name:'Use this service publication',exact:true}).click();
 await expect(page.getByLabel('Task execution',{exact:true})).toHaveValue('service');await saveWorkflow(page);await shot(page,'ui-bpmn-service-bound');
 await page.getByRole('button',{name:'Publish BPMN',exact:true}).click();await page.getByRole('button',{name:'BPMN processes',exact:true}).click();
 await page.getByRole('button',{name:'Start process',exact:true}).click();await expect(page.getByRole('button',{name:'Open run',exact:true})).toBeVisible({timeout:30000});await page.getByRole('button',{name:'Open run',exact:true}).click();
 await held(page);const originalUrl=page.url();await shot(page,'ui-held-before-restart');await command('checkpoint','held-before-restart');mark('The real dependency fault is held after first-shard file effects');
 // Publish a changed pipeline while accepted native work retains its original immutable selection.
 const newer=await context.newPage();observe(newer);await newer.goto(ready.studioUrl+'/etl/connectors');await newer.getByRole('button').filter({hasText:'direct-service-orders'}).first().click();
 await newer.getByLabel('Maximum delivery attempts',{exact:true}).fill('2');await publishEtl(newer);await expect(newer.getByRole('button',{name:/^View version 2\b/})).toBeVisible();await shot(newer,'ui-etl-published-v2');await newer.close();
 await context.close();await command('restart','restarted-held');context=await browser.newContext({viewport:{width:1440,height:1000}});page=await context.newPage();observe(page);
 await login(page,ready.studioUrl);await page.getByRole('link',{name:'BPMN runs',exact:true}).click();await expect(page.getByRole('button',{name:'Open run',exact:true})).toHaveCount(1);await page.getByRole('button',{name:'Open run',exact:true}).click();await held(page);
 if(new URL(page.url()).pathname!==new URL(originalUrl).pathname)throw new Error('Fresh discovery returned another model run.');await shot(page,'ui-held-fresh-browser');
 await page.getByRole('link',{name:'Open native attempt',exact:true}).click();await expect(page.getByRole('button').filter({hasText:/Run Etl/i}).first()).toBeVisible({timeout:30000});await page.getByRole('button').filter({hasText:/Run Etl/i}).first().click();
 const related=page.getByRole('region',{name:'Related runs'});await expect(related.getByRole('link',{name:'Open ETL run',exact:true})).toBeVisible({timeout:30000});await shot(page,'ui-original-native-attempt');await related.getByRole('link',{name:'Open ETL run',exact:true}).click();
 await expect(page.getByRole('region',{name:'Selected ETL run'})).toContainText('Faulted',{timeout:30000});await shot(page,'ui-original-etl-fault');
 await page.getByRole('link',{name:'Back to native run',exact:true}).click();await expect(page.getByRole('region',{name:'Related runs'})).toBeVisible({timeout:30000});
 await page.getByRole('link',{name:'BPMN runs',exact:true}).click();await page.getByRole('button',{name:'Open run',exact:true}).click();await held(page);await command('restore-etl-dependency','restored-dependency');
 // Deliver the command to the real server, then cut only its return response.
 let lostResponse;
 await page.route('**/bpmn/executions/*/retry-service',async route=>{const response=await route.fetch();lostResponse={status:response.status(),body:await response.json(),request:route.request().postDataJSON()};write('accepted-retry-response-cut.json',lostResponse);await route.abort('connectionreset');});
 await page.getByRole('button',{name:'Retry service',exact:true}).click();await expect(page.getByRole('button',{name:'Resume retry',exact:true})).toBeVisible({timeout:30000});
 await page.unroute('**/bpmn/executions/*/retry-service');await shot(page,'ui-retry-response-lost');await page.reload();await expect(page.getByRole('button',{name:'Resume retry',exact:true})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'Resume retry',exact:true}).click();await expect(page.getByText('Last retry: accepted (replayed).',{exact:true})).toBeVisible({timeout:30000});
 await expect(async()=>{const refresh=page.getByRole('button',{name:'Refresh run',exact:true});if(await refresh.isEnabled())await refresh.click();await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed',{timeout:500});}).toPass({timeout:60000,intervals:[200,500]});
 await expect(page.getByRole('link',{name:'Open native attempt',exact:true})).toHaveCount(2);await shot(page,'ui-parent-completed');await command('checkpoint','completed');
 await command('restart','restarted-completed');await page.reload();await expect(page.getByRole('button',{name:'Replay last retry',exact:true})).toBeVisible({timeout:30000});await page.getByRole('button',{name:'Replay last retry',exact:true}).click();await expect(page.getByText('Last retry: accepted (replayed).',{exact:true})).toBeVisible({timeout:30000});
 await shot(page,'ui-completed-replay');await command('finish','finished');mark('One replacement attempt completed; exact retry replay survived reload and host restart');
 write('browser-result.json',{passed:true,productSource:ready.sourceRevision,harnessSource:ready.harnessSourceRevision,actions,events,errors,lostResponse});
}catch(error){await currentPage?.screenshot({path:path.join(output,'browser-failure.png'),fullPage:true}).catch(()=>{});write('browser-result.json',{passed:false,error:String(error),actions,events,errors});throw error;
}finally{await Promise.all([...pending]);await browser.close();}
