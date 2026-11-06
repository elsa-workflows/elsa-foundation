const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const { buildSync } = require('esbuild');
const repo = process.env.DECISIONS_REPOSITORY || process.cwd();
const output = path.resolve(__dirname, '..', 'roundtrip');
fs.mkdirSync(output, { recursive: true });
const sourcePath = path.join(repo, 'Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionXml.ts');
const bundle = buildSync({entryPoints:[sourcePath],bundle:true,write:false,format:'iife',globalName:'ElsaDecisionXml',platform:'browser'}).outputFiles[0].text;
const digest = value => crypto.createHash('sha256').update(value).digest('hex');
(async () => {
 const browser = await chromium.launch({headless:true, executablePath:process.env.CHROMIUM_EXECUTABLE});
 try {
  const page = await browser.newPage();
  await page.route("http://localhost/interop",route=>route.fulfill({contentType:"text/html",body:'<div id="canvas" style="height:650px"></div>'}));
  await page.goto("http://localhost/interop");
  await page.addScriptTag({path:require.resolve('dmn-js/dist/dmn-modeler.development.js')});
  await page.addScriptTag({content:bundle});
  const result = await page.evaluate(async () => {
   const api = ElsaDecisionXml;
   const assert = (ok,message) => { if(!ok) throw Error(message); };
   const doc = xml => new DOMParser().parseFromString(xml,'application/xml');
   const ids = xml => [...doc(xml).querySelectorAll('[id]')].map(e=>e.getAttribute('id')).sort();
   const editExternal = async (xml,edit) => {
    const modeler = new DmnJS({container:'#canvas'});
    const imported = await modeler.importXML(xml);
    await modeler.open(modeler.getViews().find(view=>view.type==='decisionTable'));
    const viewer = modeler.getActiveViewer();
    const table = viewer.getDefinitions().drgElement[0].decisionLogic;
    edit(viewer,table);
    const saved = (await modeler.saveXML({format:true})).xml;
    const parsed = api.parseTable(saved);
    const rows = document.querySelectorAll('tr').length;
    modeler.destroy();
    return {xml:saved,warnings:imported.warnings.map(w=>w.message),rows,parsed};
   };
   let product = api.newDecisionXml();
   const productTable = api.parseTable(product);
   product = api.editTable(product,{kind:'cell',ruleId:productTable.rules[0].id,output:false,column:0,text:'<= 1000'});
   const forward = await editExternal(product,(viewer,table)=>viewer.get('modeling').editCell(table.rule[0].outputEntry[0],'"Externally approved"'));
   assert(JSON.stringify(ids(product))===JSON.stringify(ids(forward.xml)),'Forward IDs changed');
   assert(forward.parsed.rules[0].outputs[0]==='"Externally approved"','External edit absent from native parser');
   const external = `<?xml version="1.0" encoding="UTF-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" xmlns:dmndi="https://www.omg.org/spec/DMN/20191111/DMNDI/" xmlns:dc="http://www.omg.org/spec/DMN/20180521/DC/" id="external_defs" name="External approval" namespace="urn:elsa:interop"><!--covered opaque comment--><description>Preserve documentation</description><decision id="external_approval" name="External approval"><decisionTable id="external_table" hitPolicy="UNIQUE"><input id="external_amount"><inputExpression id="external_expression" typeRef="number"><text>amount</text></inputExpression></input><output id="external_output" name="result" typeRef="string"/><rule id="external_low"><inputEntry id="external_low_in"><text>&lt;= 1000</text></inputEntry><outputEntry id="external_low_out"><text>"Automatic"</text></outputEntry></rule><rule id="external_high"><inputEntry id="external_high_in"><text>&gt; 1000</text></inputEntry><outputEntry id="external_high_out"><text>"Manager"</text></outputEntry></rule></decisionTable></decision><dmndi:DMNDI><dmndi:DMNDiagram id="external_diagram"><dmndi:DMNShape id="external_shape" dmnElementRef="external_approval"><dc:Bounds x="160" y="100" width="180" height="80"/></dmndi:DMNShape></dmndi:DMNDiagram></dmndi:DMNDI></definitions>`;
   const externalEdited = await editExternal(external,(viewer,table)=>viewer.get('modeling').editCell(table.rule[0].outputEntry[0],'"External edited"'));
   const nativeEdited = api.editTable(externalEdited.xml,{kind:'cell',ruleId:'external_low',output:true,column:0,text:'"Studio approved"'});
   assert(JSON.stringify(ids(externalEdited.xml))===JSON.stringify(ids(nativeEdited)),'Native edit changed IDs');
   const inspect = xml => {
    const d=doc(xml),shape=d.getElementsByTagNameNS('https://www.omg.org/spec/DMN/20191111/DMNDI/','DMNShape')[0],bounds=d.getElementsByTagNameNS('http://www.omg.org/spec/DMN/20180521/DC/','Bounds')[0];
    return {shapeId:shape.id,reference:shape.getAttribute('dmnElementRef'),bounds:[...bounds.attributes].map(a=>[a.name,a.value]).sort(),description:d.documentElement.firstElementChild.textContent};
   };
   assert(JSON.stringify(inspect(externalEdited.xml))===JSON.stringify(inspect(nativeEdited)),'Covered diagram/documentation changed');
   const reverse = await editExternal(nativeEdited,(_viewer,table)=>assert(table.rule[0].outputEntry[0].text==='"Studio approved"','External reimport lost native edit'));
   assert(JSON.stringify(ids(nativeEdited))===JSON.stringify(ids(reverse.xml)),'External reimport changed IDs');
   assert(JSON.stringify(inspect(nativeEdited))===JSON.stringify(inspect(reverse.xml)),'External reimport changed covered diagram/documentation');
   const declared = product.replace('<decision ', '<inputData id="declared_input" name="Purchase amount"><variable id="declared_input_variable" name="amount" typeRef="number"/></inputData><decision ').replace('<decisionTable', '<variable id="declared_result" name="approval" typeRef="string"/><decisionTable');
   const declaredTable = api.parseTable(declared);
   const declaredRenamed = api.editTable(declared,{kind:'clause',id:declaredTable.inputs[0].id,output:false,name:'total',type:'number'});
   let declaredRetyped = api.editTable(declaredRenamed,{kind:'clause',id:declaredTable.inputs[0].id,output:false,name:'total',type:'string'});
   declaredRetyped = api.editTable(declaredRetyped,{kind:'cell',ruleId:declaredTable.rules[0].id,output:false,column:0,text:'-'});
   const declaredOutputs = api.editTable(declared,{kind:'add-clause',output:true});
   let declaredScalar = api.editTable(declared,{kind:'clause',id:declaredTable.outputs[0].id,output:true,name:'result',type:'number'});
   declaredScalar = api.editTable(declaredScalar,{kind:'cell',ruleId:declaredTable.rules[0].id,output:true,column:0,text:'12'});
   const uniqueInput = api.editTable(api.editTable(product,{kind:'clause',id:productTable.inputs[0].id,output:false,name:'input2',type:'number'}),{kind:'add-clause',output:false});
   let declaredInputDeletionRefused = false;
   try { api.editTable(api.editTable(declared,{kind:'add-clause',output:false}),{kind:'delete-clause',id:declaredTable.inputs[0].id,output:false}); }
   catch(error) { declaredInputDeletionRefused = error.message.includes('input-data declaration'); }
   assert(declaredInputDeletionRefused,'Declaration-backed deletion must preserve the source and refuse explicitly');
   const opaque = external.replace('<description>Preserve documentation</description>','<description>Preserve documentation</description><extensionElements><opaque:meta xmlns:opaque="urn:elsa:interop:opaque" key="keep">payload</opaque:meta></extensionElements>');
   const opaqueEdited=api.editTable(opaque,{kind:'cell',ruleId:'external_low',output:true,column:0,text:'"Preserved"'});
   const opaqueExternal=await editExternal(opaqueEdited,(viewer,table)=>viewer.get('modeling').editCell(table.rule[1].outputEntry[0],'"Manager reviewed"'));
   assert(opaqueExternal.xml.includes('urn:elsa:interop:opaque') && opaqueExternal.xml.includes('>payload<'),'Opaque subtree dropped during external edit');
   assert(opaqueEdited.includes('urn:elsa:interop:opaque') && opaqueEdited.includes('key="keep"') && opaqueEdited.includes('>payload<'),'Opaque subtree dropped during targeted native edit');
   return {product,declaredRenamed,declaredRetyped,declaredOutputs,declaredScalar,uniqueInput,forward:forward.xml,external,externalEdited:externalEdited.xml,nativeEdited,reverse:reverse.xml,opaque,opaqueEdited,opaqueExternal:opaqueExternal.xml,checks:{declaredInputDeletionRefused,forwardWarnings:forward.warnings,reverseWarnings:reverse.warnings,externalInitialWarnings:externalEdited.warnings,forwardRows:forward.rows,reverseRows:reverse.rows,forwardIdsPreserved:true,reverseIdsPreserved:true,externalReimportIdsAndDiagramPreserved:true,diagramAndDocumentationPreserved:true,opaqueNativeSubtreePreserved:true,opaqueExternalSubtreePreserved:true,opaqueExternalWarnings:opaqueExternal.warnings,reverseOutput:reverse.parsed.rules[0].outputs[0],remainingModelers:document.querySelectorAll('.dmn-js-parent').length}};
  });
  for (const name of ['product','declaredRenamed','declaredRetyped','declaredOutputs','declaredScalar','uniqueInput','forward','external','externalEdited','nativeEdited','reverse','opaque','opaqueEdited','opaqueExternal']) fs.writeFileSync(path.join(output,`${name}.dmn`),result[name]);
  const receipt={...result.checks,studioSourceSha256:digest(fs.readFileSync(sourcePath)),bundleSha256:digest(bundle),browserVersion:browser.version(),tools:{'dmn-js':require('dmn-js/package.json').version,'dmn-moddle':require('dmn-moddle/package.json').version,playwright:require('playwright/package.json').version,esbuild:require('esbuild/package.json').version},files:Object.fromEntries(Object.entries(result).filter(([k])=>k!=='checks').map(([k,v])=>[`${k}.dmn`,digest(v)]))};
  fs.writeFileSync(path.join(output,'receipt.json'),JSON.stringify(receipt,null,2)+'\n');
  console.log(JSON.stringify(receipt,null,2));
 } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
