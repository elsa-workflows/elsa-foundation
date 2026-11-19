using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
var root = "/Users/sipke/.codex/worktrees/f236/elsa-foundation/Essentials/apps/Elsa.Workbench/bin/Release/net10.0";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(root, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, name.Name + ".dll")) : null;
var bpmn=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Elsa.Activities.Bpmn.dll"));
var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Elsa.Workflows.Runtime.Core.dll"));
var model=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Bpmn.Model.dll"));
string Constant(string type,string field)=>(string)model.GetType(type)!.GetField(field)!.GetRawConstantValue()!;
var json="""
{"formatVersion":1,"identity":{"artifactId":"artifact","definitionId":"definition","definitionVersionId":"version","artifactVersion":"1.0.0","artifactHash":"hash"},"protocol":{"key":"elsa.bpmn","version":1},"profile":"foundation-native-work-v1","definition":{"formatVersion":4,"process":{"processId":"approval-process","elements":[{"elementId":"start","elementType":"startEvent"},{"elementId":"approval-task-42","elementType":"userTask","bindingRef":"approval-work-42"},{"elementId":"approval-deadline-42","elementType":"boundaryEvent","bindingRef":"timer-work","attachedToRef":"approval-task-42","cancelActivity":true,"eventDefinitions":[{"type":"timer","properties":{"interval":"PT30S"}}]},{"elementId":"end","elementType":"endEvent"}],"sequenceFlows":[{"flowId":"to-task","sourceRef":"start","targetRef":"approval-task-42"},{"flowId":"to-end","sourceRef":"approval-task-42","targetRef":"end"},{"flowId":"timeout","sourceRef":"approval-deadline-42","targetRef":"end"}]},"humanBindings":["approval-work-42"],"timerBindings":["timer-work"]},"inputContract":{"version":1,"inputs":[]},"outputs":[],"dependencies":[],"runtimeRequirements":[]}
""";
var d=JsonNode.Parse(json)!;
foreach(var pair in new[]{(0,"StartEvent"),(1,"UserTask"),(2,"BoundaryEvent"),(3,"EndEvent")}) d["definition"]!["process"]!["elements"]![pair.Item1]!["elementType"]=Constant("Bpmn.Model.BpmnElementTypes",pair.Item2);
d["definition"]!["process"]!["elements"]![2]!["eventDefinitions"]![0]!["type"]=Constant("Bpmn.Model.BpmnEventDefinitionTypes","Timer");
var interval=Constant("Bpmn.Model.BpmnEventDefinitionProperties","Interval");
d["definition"]!["process"]!["elements"]![2]!["eventDefinitions"]![0]!["properties"]=new JsonObject{[interval]="PT30S"};
void Probe(string name,JsonNode n) {
 var material=JsonSerializer.Deserialize(n.ToJsonString(),core.GetType("Elsa.Workflows.Runtime.Core.Models.RuntimeModelMaterial")!,new JsonSerializerOptions(JsonSerializerDefaults.Web));
 try{bpmn.GetType("Elsa.Activities.Bpmn.Internal.BpmnDirectExecutionDriver")!.GetMethod("Graph",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[material]); Console.WriteLine(name+": ACCEPTED");}
 catch(TargetInvocationException e){Console.WriteLine(name+": "+e.InnerException!.GetType().FullName+": "+e.InnerException.Message);}
}
Probe("complete-inventory-control",d);
var bad=d.DeepClone();bad["definition"]!["humanBindings"]![0]="unrelated-work";Probe("missing-approval-binding",bad);
foreach(var f in new[]{"Elsa.Activities.Bpmn.dll","Elsa.Workflows.Runtime.Core.dll","Bpmn.Model.dll"}) Console.WriteLine(f+" "+FileVersionInfo.GetVersionInfo(Path.Combine(root,f)).ProductVersion+" "+Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,f)))));
