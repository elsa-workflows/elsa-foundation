// Read-only UI inspection of a completed owned ETL recovery fixture; creates no execution or command.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
const repo=process.cwd(),root=path.resolve(process.argv[2]),ready=JSON.parse(fs.readFileSync(path.join(root,'ready.json'),'utf8'));
const { chromium,expect }=await import(pathToFileURL(path.join(repo,'Studio/node_modules/@playwright/test/index.mjs')).href);
const browser=await chromium.launch({headless:true}),context=await browser.newContext({viewport:{width:1440,height:1000}}),page=await context.newPage();page.setDefaultTimeout(30000);
const writes=[],errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(['POST','PUT','DELETE'].includes(r.method()) && /\/(bpmn|etl|runtime)\//.test(new URL(r.url()).pathname))writes.push({method:r.method(),path:new URL(r.url()).pathname});});
try{
 await page.goto(ready.studioUrl+'/workflows/bpmn/runs');const username=page.getByLabel('Username',{exact:true});
 if(await username.waitFor({state:'visible',timeout:5000}).then(()=>true,()=>false)){await username.fill('admin');await page.getByLabel('Password',{exact:true}).fill('Password123!');await page.getByRole('button',{name:'Sign in',exact:true}).click();}
 await page.waitForURL(u=>u.origin===ready.studioUrl);await expect(page.getByRole('button',{name:'Open run',exact:true})).toHaveCount(1);await page.getByRole('button',{name:'Open run',exact:true}).click();
 await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed');
 await page.locator('.wf-bpmn-run li').filter({hasText:/^Completed/}).getByRole('link',{name:'Open native attempt',exact:true}).click();
 await expect(page.getByRole('button').filter({hasText:/Run Etl/i}).first()).toBeVisible();await page.getByRole('button').filter({hasText:/Run Etl/i}).first().click();
 const related=page.getByRole('region',{name:'Related runs'});await expect(related.getByRole('link',{name:'Open ETL run',exact:true})).toBeVisible();await related.screenshot({path:path.join(root,'ui-replacement-native-reference.png')});await related.getByRole('link',{name:'Open ETL run',exact:true}).click();
 const detail=page.getByRole('region',{name:'Selected ETL run'});await expect(detail).toContainText('Succeeded');
 const metric=label=>detail.getByText(label,{exact:true}).locator('xpath=following-sibling::dd[1]');
 await expect(metric('Accepted records')).toHaveText('4');const selection=JSON.parse(fs.readFileSync(path.join(root,'pipeline-selection.json'),'utf8')).selection;await expect(metric('Published pipeline')).toHaveText(selection);
 await page.screenshot({path:path.join(root,'ui-replacement-etl-success.png'),fullPage:true});const url=page.url(),text=await detail.innerText();
 // Upgrade inspection also keeps the retained-private-state path: the original failed attempt still links to its faulted ETL run.
 let original=null;
 if(process.argv.includes('--with-original')){
  await page.getByRole('link',{name:'BPMN runs',exact:true}).click();await page.getByRole('button',{name:'Open run',exact:true}).click();await expect(page.locator('.wf-bpmn-run-heading')).toContainText('Completed');
  await page.locator('.wf-bpmn-run li').filter({hasText:/^Faulted/}).getByRole('link',{name:'Open native attempt',exact:true}).click();
  await expect(page.getByRole('button').filter({hasText:/Run Etl/i}).first()).toBeVisible();await page.getByRole('button').filter({hasText:/Run Etl/i}).first().click();
  const originalRelated=page.getByRole('region',{name:'Related runs'});await expect(originalRelated.getByRole('link',{name:'Open ETL run',exact:true})).toBeVisible();await originalRelated.getByRole('link',{name:'Open ETL run',exact:true}).click();
  const fault=page.getByRole('region',{name:'Selected ETL run'});await expect(fault).toContainText('Faulted');await expect(fault.getByText('Published pipeline',{exact:true}).locator('xpath=following-sibling::dd[1]')).toHaveText(selection);
  await page.screenshot({path:path.join(root,'ui-original-etl-fault.png'),fullPage:true});original={url:page.url(),text:await fault.innerText()};
 }
 const result={passed:true,original,driverSource:execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim(),productSource:ready.sourceRevision,url,text,selection,acceptedRecords:4,writes,errors};
 if(writes.length||errors.length)throw new Error('Read-only replacement inspection produced a mutation or page error.');
 fs.writeFileSync(path.join(root,'replacement-browser-inspection.json'),JSON.stringify(result,null,2)+'\n');
}catch(e){await page.screenshot({path:path.join(root,'replacement-inspection-failure.png'),fullPage:true}).catch(()=>{});throw e;
}finally{await browser.close();}
