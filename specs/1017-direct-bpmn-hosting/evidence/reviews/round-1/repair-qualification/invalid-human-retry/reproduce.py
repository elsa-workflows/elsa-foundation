import hashlib,http.cookiejar,json,os,pathlib,re,shutil,subprocess,time,urllib.error,urllib.request
repo=pathlib.Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation')
out=pathlib.Path(__file__).parent
root=out/'host'; root.mkdir(exist_ok=False); (root/'packages').mkdir()
source=repo/'Essentials/apps/Elsa.Workbench'
assembly=source/'bin/Release/net10.0/Elsa.Workbench.dll'
reader=repo/'tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll'
def save(name,data): (out/name).write_text(json.dumps(data,indent=2)+'\n')
for name in ['appsettings.json','appsettings.Development.json','shells.json']: shutil.copyfile(source/name,root/name)
shells=json.loads((root/'shells.json').read_text()); features=shells['CShells']['Shells']['default']['Features']
allowed={'ActivitiesBpmnHosting','ActivitiesBpmnHostingApi','ActivitiesRuntime','Events','Expressions','FileSystemDistributedLocking','Primitives','Serialization','Tasks','WorkflowsRuntimeEntityFrameworkCore','WorkflowsRuntimeResumption','FoundationIdentityAbstractions','FoundationIdentityApi','FoundationIdentityAspNetCoreIdentity','FoundationIdentityAspNetCoreIdentityEntityFrameworkCore','IdentityIamEntityFrameworkCore','IdentityProviderConfigurationEntityFrameworkCore'}
for name in list(features):
 if name not in allowed: del features[name]
features.update({'ActivitiesBpmnHosting':{'AdmitNewExecutions':True},'ActivitiesBpmnHostingApi':{},'WorkflowsRuntimeResumption':{'SweepIntervalSeconds':300}})
features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString']='Data Source=runtime.db;Pooling=False'
(root/'shells.json').write_text(json.dumps(shells,indent=2))
products=subprocess.run(['dotnet',str(reader),'--inspect-host-products',str(assembly)],capture_output=True,text=True,timeout=30)
(out/'products.json').write_text(products.stdout); assert products.returncode==0,products.stderr
meta=json.loads(products.stdout); assert meta['sourceCommit']=='7fc0217a4520216834125c9b94b3a851fb02841c',meta
log=(out/'host.log').open('w'); child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(root),'--urls','http://127.0.0.1:0'],cwd=root,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT)
process={'processId':child.pid,'controllerProcessId':os.getpid(),'command':['dotnet',str(assembly),'--contentRoot',str(root),'--urls','http://127.0.0.1:0'],'startedAt':time.time()}
responses=[]; base=None
opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def request(path,body=None):
 req=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers={} if body is None else {'Content-Type':'application/json'})
 try:
  response=opener.open(req,timeout=30); status=response.status; text=response.read().decode()
 except urllib.error.HTTPError as e: status=e.code; text=e.read().decode()
 try: data=json.loads(text)
 except: data=text
 responses.append({'path':path,'request':body,'status':status,'response':data})
 save('http.json',responses)
 return status,data
def read(label,execution):
 r=subprocess.run(['dotnet',str(reader),'--read-bpmn-direct',str(root/'runtime.db'),execution],capture_output=True,text=True,timeout=30)
 (out/(label+'-checked.json')).write_text(r.stdout); assert r.returncode==0,r.stderr
 return json.loads(r.stdout)
try:
 limit=time.monotonic()+60
 while time.monotonic()<limit:
  assert child.poll() is None,'host exited'
  match=re.search(r'Now listening on: (http://127\.0\.0\.1:\d+)',(out/'host.log').read_text())
  if match: base=match.group(1);break
  time.sleep(.1)
 assert base,'no listening address'
 limit=time.monotonic()+60
 while time.monotonic()<limit:
  status,data=request('/health/ready')
  if status==200:break
  time.sleep(.2)
 assert status==200
 assert request('/_elsa/identity/login',{'username':'admin','password':'Password123!'})[0]==200
 definition={'formatVersion':4,'humanBindings':['review-work'],'serviceBindings':[],'timerBindings':['timeout-work'],'process':{'processId':'retry-human-empty-services','elements':[{'elementId':'start','elementType':'startEvent'},{'elementId':'review','elementType':'userTask','bindingRef':'review-work'},{'elementId':'timeout','elementType':'boundaryEvent','bindingRef':'timeout-work','attachedToRef':'review','cancelActivity':True,'eventDefinitions':[{'type':'timer','properties':{'interval':'PT1H'}}]},{'elementId':'done','elementType':'endEvent'},{'elementId':'timed-out','elementType':'endEvent'}],'sequenceFlows':[{'flowId':'start-review','sourceRef':'start','targetRef':'review'},{'flowId':'review-done','sourceRef':'review','targetRef':'done'},{'flowId':'timeout-end','sourceRef':'timeout','targetRef':'timed-out'}]}}
 save('definition.json',definition)
 status,published=request('/bpmn/artifacts',{'requestId':'publish-human-empty','definition':definition});assert status==200,(status,published)
 status,started=request('/bpmn/executions',{'requestId':'start-human-empty','sourceReferenceId':published['sourceReferenceId']});assert status==201,(status,started)
 execution=started['executionId']; route='/bpmn/executions/'+execution
 before=read('before',execution)
 status,listed=request(route+'/work');assert status==200 and len(listed['items'])==1,(status,listed)
 human=listed['items'][0]
 status,retry=request(route+'/retry-service',{'requestId':'invalid-retry','incidentId':'nonexistent-incident','invocationId':human['invocationId'],'failedAttemptId':human['attemptId']});save('retry-response.json',{'status':status,'response':retry})
 after=read('after-retry',execution)
 status,complete=request(route+'/work/'+human['invocationId']+'/complete',{'requestId':'normal-complete','attemptId':human['attemptId']});save('complete-response.json',{'status':status,'response':complete})
 status,cancel=request(route+'/cancel',{'requestId':'normal-cancel','reason':'ordinary cancellation after invalid retry'});save('cancel-response.json',{'status':status,'response':cancel})
 final=read('final',execution)
 save('summary.json',{'executionId':execution,'publicationStatus':responses[-7]['status'] if len(responses)>=7 else None,'retry':retry,'complete':complete,'cancel':cancel,'beforeRevision':before['model']['revision'],'afterRevision':after['model']['revision'],'finalRevision':final['model']['revision'],'finalLifecycle':final['model']['lifecycle'],'queuedInputs':final.get('queuedModelInputs'),'inputKinds':[x['inputKind'] for x in final['inputs']],'stateSeeded':False,'clockEdited':False,'sourceStateEdited':False,'actualSourceRevision':meta['sourceCommit']})
 print(json.dumps({'executionId':execution,'retry':retry,'complete':complete,'cancel':cancel,'beforeRevision':before['model']['revision'],'finalRevision':final['model']['revision'],'queuedInputs':len(final.get('queuedModelInputs',[])),'inputKinds':[x['inputKind'] for x in final['inputs']]}))
 assert final['model']['lifecycle']==3 and final['model']['terminalResult']['status']=='Completed',final
 assert not final['queuedModelInputs'],final
 receipt=next(x for x in final['inputs']if x['inputId']=='invalid-retry')
 assert receipt['result']['status']=='Rejected'and receipt['result']['code']=='incident-not-current',receipt
 status,started2=request('/bpmn/executions',{'requestId':'start-human-empty-cancel','sourceReferenceId':published['sourceReferenceId']});assert status==201,(status,started2)
 execution2=started2['executionId'];route2='/bpmn/executions/'+execution2
 status,listed2=request(route2+'/work');assert status==200 and len(listed2['items'])==1
 human2=listed2['items'][0]
 status,retry2=request(route2+'/retry-service',{'requestId':'invalid-retry','incidentId':'nonexistent-incident','invocationId':human2['invocationId'],'failedAttemptId':human2['attemptId']})
 status,cancel2=request(route2+'/cancel',{'requestId':'following-cancel','reason':'ordinary cancellation after invalid retry'})
 final2=read('final-cancel',execution2)
 assert final2['model']['lifecycle']==3 and final2['model']['terminalResult']['status']=='Cancelled',final2
 assert not final2['queuedModelInputs']
 assert next(x for x in final2['inputs']if x['inputId']=='invalid-retry')['result']['status']=='Rejected'
 save('green-summary.json',{'source':meta['sourceCommit'],'completionExecution':execution,'cancellationExecution':execution2,'completionRevision':final['model']['revision'],'cancellationRevision':final2['model']['revision'],'retryRejectedDurably':True,'bothFollowingCommandsAccepted':True,'queuedInputs':0,'seededState':False,'editedClock':False})
 print('Both actual invalid retry -> complete/cancel paths passed.')
finally:
 if child.poll() is None: child.terminate();child.wait(timeout=30)
 process.update({'exitCode':child.returncode,'stoppedAt':time.time(),'baseAddress':base});save('process.json',process);log.close()
