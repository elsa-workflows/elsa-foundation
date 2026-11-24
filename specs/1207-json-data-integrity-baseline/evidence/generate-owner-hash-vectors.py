"""Independent fixed owner projections. No production CLR serializer or hasher is loaded.

These recipes and their owned contracts freeze at the first stable release. Changes
after that release require an explicit identity/migration decision.
"""
from pathlib import Path
import base64, hashlib, hmac, importlib.util, json
spec=importlib.util.spec_from_file_location('canonical_fixture',Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)

def generate():
    variants=[('base','{"OwnerKey":"René","n":9007199254740993,"rows":[1,null]}'),
        ('spelling',' { "rows": [1e0,null], "n":9007199254740993.0,"OwnerKey":"Ren\\u00e9" } '),
        ('missing','{"OwnerKey":"René","n":9007199254740993}'),
        ('null','{"OwnerKey":"René","n":9007199254740993,"rows":null}'),
        ('order','{"OwnerKey":"René","n":9007199254740993,"rows":[null,1]}'),
        ('number','{"OwnerKey":"René","n":9007199254740992,"rows":[1,null]}')]
    owners=[
        ('delivery','Elsa.Workflows.Runtime.Core','Elsa.Workflows.Runtime.Core.Models.RuntimeDeliveryFingerprint','Compute','domain'),
        ('trigger','Elsa.Workflows.Runtime.Api','Elsa.Workflows.Runtime.Api.StandaloneTriggers.TriggerManagement','Fingerprint','json'),
        ('dashboard-resource','Elsa.Studio.Dashboard.Management','Elsa.Studio.Dashboard.Management.Resources.DashboardResourceService','Fingerprint','json'),
        ('release-manifest','Elsa.Workflows.Publishing.Releases','Elsa.Workflows.Publishing.Releases.ReleaseManifest','HashManifest','json'),
        ('release-member','Elsa.Workflows.Publishing.Releases','Elsa.Workflows.Publishing.Releases.ReleaseManifest','Hash','utf8')]
    result=[]
    for owner,assembly,type_name,method,kind in owners:
        for name,raw in variants:
            value=helper.load_json(raw)
            projection=['elsa.test.delivery.v1',value] if kind=='domain' else value
            canonical=helper.canonical(projection)
            digest=hashlib.sha256(canonical.encode()).hexdigest()
            if owner.startswith('release-'):digest='sha256:'+digest
            args=[{'utf8':raw}] if kind=='utf8' else [{'json':json.loads(raw)}]
            # Keep spelling witnesses as literal JSON strings for the CLR parser.
            if kind!='utf8':args=[{'rawJson':raw}]
            if kind=='domain':args.insert(0,{'text':'elsa.test.delivery.v1'})
            result.append(dict(name=owner+'/'+name,assembly=assembly,type=type_name,method=method,
                arguments=args,canonical=canonical,expected=digest))
    def add(name, assembly, type_name, method, arguments, projection, **extra):
        canonical=helper.canonical(projection)
        result.append(dict(name=name,assembly=assembly,type=type_name,method=method,arguments=arguments,
            canonical=canonical,expected=hashlib.sha256(canonical.encode()).hexdigest(),**extra))
    unicode_raw='{"β":"é","null":null,"items":[1,true,"<"]}'
    add('release-manifest/unicode','Elsa.Workflows.Publishing.Releases','Elsa.Workflows.Publishing.Releases.ReleaseManifest',
        'HashManifest',[{'rawJson':unicode_raw}],helper.load_json(unicode_raw))
    result[-1]['expected']='sha256:'+result[-1]['expected']
    for name, settings in [('base',{'Authority':'https://é.example','ClientId':'MixedCase'}),
                           ('key-case',{'authority':'https://é.example','ClientId':'MixedCase'})]:
        record=dict(key='company',protocol='oidc',settings=settings,secrets={},admission=dict(type='reject',defaultRoleIds=[],matcher=None),
                    enabled=True,overridesDeployment=False,archivedAt=None)
        add('connection-material/'+name,'Elsa.Foundation.Identity.ExternalAuthentication',
            'Elsa.Foundation.Identity.ExternalAuthentication.Connections.ConnectionSelection','MaterialRevision',[{'json':record}],record)
    for name, actor in [('base',dict(tenantId='tenant-é',userId='reader',roleIds=[],permissions=[])),
                        ('different-subject',dict(tenantId='tenant-é',userId='other',roleIds=[],permissions=[]))]:
        add('audience-scope/'+name,'Elsa.Studio.Dashboard.Api','Elsa.Studio.Dashboard.Api.DashboardAudienceCursors','Scope',
            [{'json':actor},{'text':'role'},{'text':'Mixed'}],[actor['tenantId'],actor['userId'],'role','Mixed'])
    key=bytes(range(32))
    actor=dict(tenantId='tenant-é',subjectId='reader')
    projection=[actor['tenantId'],actor['subjectId'],'after-key']
    canonical=helper.canonical(projection)
    result.append(dict(name='processing-cursor/fixed-key',assembly='Acme.Processing',type='Acme.Processing.ProcessingRecords',
        method='SignWithKey',arguments=[{'json':actor},{'text':'after-key'},{'keyHex':key.hex()}],canonical=canonical,
        expected=base64.b64encode(hmac.digest(key,canonical.encode(),'sha256')).decode()))
    payload='{"scope":"scope-é","skip":7}'
    canonical=helper.canonical(helper.load_json(payload))
    result.append(dict(name='audience-cursor/fixed-key',assembly='Elsa.Studio.Dashboard.Api',type='Elsa.Studio.Dashboard.Api.DashboardAudienceCursors',
        method='Write',instanceKeyHex=key.hex(),arguments=[{'text':'scope-é'},{'json':7}],canonical=canonical,retainedPayload=payload,
        expected=base64.b64encode(payload.encode()).decode()+'.'+base64.b64encode(hmac.digest(key,canonical.encode(),'sha256')).decode()))
    return result

if __name__=='__main__':
    Path(__file__).with_name('owner-hash-vectors.json').write_text(json.dumps(generate(),ensure_ascii=True,indent=2)+'\n',encoding='utf-8')
