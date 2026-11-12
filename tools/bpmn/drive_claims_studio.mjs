// Real Studio claims and interruption acceptance for the owned fresh direct-human fixture; no mocked network or domain-state writes.
// Every claim and claimed completion is an actual UI action. A separately signed-in operator API client only publishes the
// timed fixture, starts runs, cancels one run and records refusals, because Studio has no timer authoring or operator console.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
const repo=process.cwd();
const { chromium, request, expect }=await import(pathToFileURL(path.join(repo,'Studio/node_modules/@playwright/test/index.mjs')).href);
if (!process.argv[2]) throw new Error('Usage: node tools/bpmn/drive_claims_studio.mjs OUTPUT [INPUT_BPMN]');
const output=path.resolve(process.argv[2]);
const input=path.resolve(process.argv[3] ?? 'tools/bpmn/fixtures/direct-human.bpmn');
const ready=JSON.parse(fs.readFileSync(path.join(output,'ready.json'),'utf8'));
if(ready.family!=='direct-human')throw new Error('Expected the owned fresh direct-human fixture.');
const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
fs.writeFileSync(path.join(output,'browser-started.json'),JSON.stringify({sourceRevision:ready.sourceRevision,driverSha256:hash(fileURLToPath(import.meta.url)),inputSha256:hash(input)})+'\n',{flag:'wx'});
const events=[], actions=[], consoles=[], probes={};
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
 write(name+'.json',{url:page.url(),text:await page.locator('body').innerText(),viewport:page.viewportSize()});
}
async function login(page,url,username='admin'){
 await page.goto(url);
 const field=page.getByLabel('Username',{exact:true});
 const visible=await field.waitFor({state:'visible',timeout:5000}).then(()=>true,()=>false);
 if(visible){await field.fill(username);await page.getByLabel('Password',{exact:true}).fill('Password123!');await page.getByRole('button',{name:'Sign in',exact:true}).click();}
 await page.waitForURL(value=>value.origin===ready.studioUrl,{timeout:30000});
 await expect(page.getByRole('main').first()).toBeVisible({timeout:30000});
}
async function command(action,label,extra={}){
 const receipt=path.join(output,(action==='finish'?'process':label)+'.json');
 if(fs.existsSync(receipt))throw new Error('Refusing to overwrite an existing capture.');
 fs.writeFileSync(path.join(output,'command.json'),JSON.stringify({action,...(label?{label}:{}),...extra}));
 const end=Date.now()+180000;
 while(!fs.existsSync(receipt)){if(Date.now()>end)throw new Error('Owned controller did not acknowledge '+action);await new Promise(resolve=>setTimeout(resolve,200));}
 mark('Controller '+action+(label?' '+label:''));
}
// The operator API client signs in directly to the owned backend; it never acts in the Studio session.
let operator;
async function signInOperator(){
 operator=await request.newContext({baseURL:ready.backendUrl});
 const response=await operator.post('/_elsa/identity/login',{data:{username:'admin',password:'Password123!'}});
 if(!response.ok())throw new Error('Operator API sign-in returned HTTP '+response.status());
}
async function api(method,route,data){
 const response=await operator.fetch(route,{method,...(data?{data}:{})});let body=null;try{body=await response.json();}catch{body=null;}
 return {status:response.status(),body};
}
async function refreshUntil(page,text){
 await expect(async()=>{
  const refresh=page.getByRole('button',{name:'Refresh run',exact:true});
  if(await refresh.isEnabled())await refresh.click();
  await expect(page.locator('.wf-bpmn-run-heading')).toContainText(text,{timeout:1000});
 }).toPass({timeout:90000,intervals:[500,1000,2000]});
}
async function openRun(page,runUrl,executionId){
 const url=new URL(runUrl);url.pathname=url.pathname.replace(/[^/]+$/,encodeURIComponent(executionId));
 await page.goto(url.toString());await expect(page.locator('.wf-bpmn-run-heading')).toBeVisible({timeout:30000});
}
async function claimInUi(page,keyboard=false){
 await expect(page.getByText('Unclaimed. Claim this work before completing it.',{exact:true})).toBeVisible({timeout:30000});
 await expect(page.getByRole('button',{name:'Complete work',exact:true})).toHaveCount(0);
 const claim=page.getByRole('button',{name:'Claim work',exact:true});
 if(keyboard){
  let tabs=0;for(;tabs<100;tabs++){await page.keyboard.press('Tab');if(await claim.evaluate(element=>element===document.activeElement))break;}
  if(tabs===100)throw new Error('Claim work could not be reached by keyboard.');
  probes.claimKeyboardTabs=tabs+1;await page.keyboard.press('Enter');
 } else await claim.click();
 await expect(page.getByText('Last claim: accepted.',{exact:true})).toBeVisible({timeout:30000});
 await expect(page.getByText('Claimed by you.',{exact:true})).toBeVisible({timeout:30000});
}
const claimOf=async executionId=>{const work=(await api('GET','/bpmn/executions/'+encodeURIComponent(executionId)+'/work')).body.items;return work[0];};
try{
 await signInOperator();
 let context=await browser.newContext({viewport:{width:1280,height:900}});
 let page=await context.newPage();observe(page);
 await login(page,ready.studioUrl+'/workflows/definitions');mark('Signed in through the real form');
 await page.getByRole('button',{name:'Create workflow',exact:true}).first().click();
 const dialog=page.getByRole('dialog',{name:/Create Workflow/i});
 await dialog.getByLabel('Display name',{exact:true}).fill('Claimed approval journey');
 await dialog.getByText('BPMN',{exact:true}).click();await dialog.getByRole('button',{name:'Create',exact:true}).click();
 await expect(page.locator('.wf-editor-toolbar')).toBeVisible({timeout:30000});
 const definitionUrl=page.url();
 await page.getByLabel('Import BPMN file',{exact:true}).setInputFiles(input);
 await expect(page.locator('.react-flow__node[data-id="review"]')).toContainText('Review order',{timeout:30000});
 await page.locator('.react-flow__node[data-id="review"]').click();
 await page.getByLabel('Task execution',{exact:true}).selectOption('human');
 const requireClaim=page.getByRole('checkbox',{name:'Require a claim before completion',exact:true});
 await requireClaim.check();await expect(requireClaim).toBeChecked();
 await page.getByRole('button',{name:'Save',exact:true}).click();
 await expect(page.locator('.wf-editor-save-status')).toHaveText(/^(Saved|Autosaved)$/,{timeout:30000});
 await page.reload();await expect(page.locator('.wf-editor-toolbar')).toBeVisible({timeout:30000});
 await page.locator('.react-flow__node[data-id="review"]').click();
 await expect(page.getByRole('checkbox',{name:'Require a claim before completion',exact:true})).toBeChecked();
 await shot(page,'ui-claim-authored');mark('Authored, saved and reopened a claim-required human wait');
 await page.getByRole('button',{name:'Publish BPMN',exact:true}).click();
 await expect(page.getByRole('button',{name:'BPMN processes',exact:true})).toBeVisible({timeout:30000});
 await Promise.all([...pendingReads]);const publication=events.find(event=>event.path.endsWith('/publish-direct')&&event.status<300);
 if(publication?.response?.profile!=='foundation-direct-claims-v1')throw new Error('Studio did not publish the claim-required profile.');
 await page.getByRole('button',{name:'BPMN processes',exact:true}).click();
 await page.getByRole('button',{name:'Start process',exact:true}).click();
 await expect(page.getByRole('button',{name:'Open run',exact:true})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'Open run',exact:true}).click();
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Waiting for a person',{timeout:30000});
 const runUrl=page.url(),executionId=decodeURIComponent(new URL(runUrl).pathname.split('/').pop());
 await shot(page,'ui-claim-unclaimed');await claimInUi(page,true);await shot(page,'ui-claimed');mark('Claimed with Tab and Enter');
 for(const mode of ['light','dark']){
  await page.setViewportSize({width:1280,height:900});await page.getByRole('radio',{name:mode==='light'?'Light':'Dark',exact:true}).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme-mode',mode);
  for(const width of [1280,320]){await page.setViewportSize({width,height:900});await expect(page.getByText('Claimed by you.',{exact:true})).toBeVisible();await shot(page,`ui-claimed-${mode}-${width}`);
   const size=await page.evaluate(()=>({width:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
   if(size.scroll>size.width+1)throw new Error(`Horizontal overflow at ${mode}/${width}: ${JSON.stringify(size)}`);
  }
 }
 await page.setViewportSize({width:1280,height:900});await page.getByRole('radio',{name:'Light',exact:true}).click();
 const owner=(await claimOf(executionId)).claim;mark('Claimed through Studio');await command('checkpoint','claimed');

 // A real restart that seeds a second administrator; that subject sees the owner and cannot complete.
 await context.close();await command('restart','restarted-claimed',{seedAdministrator:'operator'});
 context=await browser.newContext({viewport:{width:1280,height:900}});page=await context.newPage();observe(page);
 await login(page,ready.studioUrl,'operator');await page.getByRole('link',{name:'BPMN runs',exact:true}).click();
 await page.getByRole('button',{name:'Open run',exact:true}).click();
 await expect(page.getByText(`Claimed by ${owner.claimantId}.`,{exact:true})).toBeVisible({timeout:30000});
 const blocked=page.getByRole('button',{name:'Complete work',exact:true});
 await expect(blocked).toBeDisabled();await expect(blocked).toHaveAttribute('title','Only the current owner can complete this work.');
 await shot(page,'ui-other-owner');mark('A second subject sees the owner and cannot complete');
 await context.close();

 context=await browser.newContext({viewport:{width:1280,height:900}});page=await context.newPage();observe(page);
 await login(page,runUrl);await expect(page.getByText('Claimed by you.',{exact:true})).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'Complete work',exact:true}).click();
 await expect(page.getByText('Last completion: accepted.',{exact:true})).toBeVisible({timeout:30000});
 await refreshUntil(page,'Completed');await expect(page.getByText('No outstanding work.',{exact:true})).toBeVisible();
 await shot(page,'ui-claimed-completed');await command('checkpoint','completed');
 await command('restart','restarted-completed');await page.reload();
 if(new URL(page.url()).origin!==ready.studioUrl)await login(page,page.url());
 await page.getByRole('button',{name:'Replay last completion',exact:true}).click();
 await expect(page.getByText('Last completion: accepted (replayed).',{exact:true})).toBeVisible({timeout:30000});
 await shot(page,'ui-claimed-replayed');mark('The owner completed; the completion replays after a real restart');
 await signInOperator();

 // Timer interruption of claimed work: raw format 6 material with a duration boundary, operated in Studio.
 const element=(id,type,extra={})=>({elementId:id,elementType:type,...extra});
 const timed={formatVersion:6,humanBindings:['review-work'],timerBindings:['timeout-work'],claimBindings:['review-work'],process:{processId:'timed-claimed-review',
  elements:[element('start','startEvent'),element('review','userTask',{name:'Review order',bindingRef:'review-work'}),
   element('timeout','boundaryEvent',{bindingRef:'timeout-work',attachedToRef:'review',cancelActivity:true,eventDefinitions:[{type:'timer',properties:{interval:'PT20S'}}]}),
   element('done','endEvent'),element('timed-out','endEvent')],
  sequenceFlows:[{flowId:'start-review',sourceRef:'start',targetRef:'review'},{flowId:'review-done',sourceRef:'review',targetRef:'done'},{flowId:'timeout-end',sourceRef:'timeout',targetRef:'timed-out'}]}};
 probes.timedPublication=await api('POST','/bpmn/artifacts',{requestId:'timed-claims',definition:timed});
 if(probes.timedPublication.status>=300||probes.timedPublication.body.profile!=='foundation-direct-claims-v1')throw new Error('The timed claim fixture was not published.');
 probes.timedStart=await api('POST','/bpmn/executions',{requestId:'timed-start',sourceReferenceId:probes.timedPublication.body.sourceReferenceId});
 const timedId=probes.timedStart.body.executionId;
 await openRun(page,runUrl,timedId);await claimInUi(page);const timedClaim=(await claimOf(timedId)).claim;
 await shot(page,'ui-timer-claimed');await command('checkpoint','timer-claimed');
 await refreshUntil(page,'Completed');await expect(page.getByText('No outstanding work.',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Complete work',exact:true})).toHaveCount(0);
 const timedInspection=(await api('GET','/bpmn/executions/'+encodeURIComponent(timedId)+'/inspection')).body;
 probes.timerInspection={status:timedInspection.execution.status,result:timedInspection.execution.result,work:timedInspection.work};
 await Promise.all([...pendingReads]);const claimEvent=events.findLast(event=>event.path.endsWith('/claim'));
 probes.lateClaimedCompletion=await api('POST',claimEvent.path.replace(/\/claim$/,'/complete-claimed'),{requestId:'late-after-timer',attemptId:claimEvent.request.attemptId,claimId:timedClaim.claimId});
 // The timer path ends the process, so the late claimed completion meets the terminal execution first.
 if(probes.lateClaimedCompletion.status!==409||probes.lateClaimedCompletion.body.result.code!=='execution-terminal')throw new Error('Interrupted claimed work was not refused.');
 await shot(page,'ui-timer-interrupted');await command('checkpoint','timer-interrupted');mark('The duration timer interrupted claimed work; the late claimed completion is refused');

 // Cancellation interruption of claimed work through the authorized API, observed in Studio.
 probes.cancelStart=await api('POST','/bpmn/executions',{requestId:'cancel-start',sourceReferenceId:publication.response.sourceReferenceId});
 const cancelId=probes.cancelStart.body.executionId;
 await openRun(page,runUrl,cancelId);await claimInUi(page);const cancelClaim=(await claimOf(cancelId)).claim;
 await Promise.all([...pendingReads]);const cancelClaimEvent=events.findLast(event=>event.path.endsWith('/claim'));
 probes.cancel=await api('POST','/bpmn/executions/'+encodeURIComponent(cancelId)+'/cancel',{requestId:'operator-cancel',reason:'Withdrawn order'});
 await refreshUntil(page,'Cancelled');await expect(page.getByRole('button',{name:'Complete work',exact:true})).toHaveCount(0);
 probes.claimedCompletionAfterCancel=await api('POST',cancelClaimEvent.path.replace(/\/claim$/,'/complete-claimed'),{requestId:'late-after-cancel',attemptId:cancelClaimEvent.request.attemptId,claimId:cancelClaim.claimId});
 if(probes.claimedCompletionAfterCancel.status!==409||probes.claimedCompletionAfterCancel.body.result.code!=='execution-terminal')throw new Error('Cancelled claimed work was not refused.');
 await shot(page,'ui-cancel-interrupted');await command('checkpoint','cancel-interrupted');mark('Cancellation interrupted claimed work; a late claimed completion is refused');

 await Promise.all([...pendingReads]);if(consoles.length)throw new Error('Browser page errors were recorded.');
 write('browser-receipt.json',{sourceRevision:ready.sourceRevision,definitionUrl,runUrl,executionId,owner,timedExecutionId:timedId,cancelledExecutionId:cancelId,actions,events,probes,pageErrors:consoles,completed:true});
 mark('Real Studio claims and interruption journey complete.');
}catch(error){
 if(currentPage){await shot(currentPage,'ui-failure').catch(()=>{});}
 await Promise.all([...pendingReads]);write('browser-failure.json',{sourceRevision:ready.sourceRevision,error:String(error),actions,events,probes,pageErrors:consoles});
 console.error(error);process.exitCode=1;
}finally{await operator?.dispose();await browser.close();}
