using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
var root = "/Users/sipke/.codex/worktrees/f236/elsa-foundation/Essentials/tests/Activities/Bpmn/Tests/bin/Release/net10.0";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(root, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, name.Name + ".dll")) : null;
var bpmn = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Elsa.Activities.Bpmn.dll"));
var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Elsa.Workflows.Runtime.Core.dll"));
var model = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Bpmn.Model.dll"));
var processType = model.GetType("Bpmn.Model.BpmnProcessDefinition")!;
Console.WriteLine("process members: " + string.Join(",", processType.GetProperties().Select(p=>p.Name)));
var elementType = model.GetType("Bpmn.Model.BpmnElement")!;
Console.WriteLine("element members: " + string.Join(",", elementType.GetProperties().Select(p=>p.Name)));
var json = """
{"formatVersion":1,"identity":{"artifactId":"artifact","definitionId":"definition","definitionVersionId":"version","artifactVersion":"1.0.0","artifactHash":"hash"},"protocol":{"key":"elsa.bpmn","version":1},"profile":"foundation-direct-v1","definition":{"formatVersion":1,"process":{"processId":"approval-process","elements":[{"elementId":"start","elementType":"startEvent"},{"elementId":"unsupported-call-42","elementType":"callActivity"},{"elementId":"end","elementType":"endEvent"}],"sequenceFlows":[{"flowId":"to-call","sourceRef":"start","targetRef":"unsupported-call-42"},{"flowId":"to-end","sourceRef":"unsupported-call-42","targetRef":"end"}]},"humanBindings":[]},"inputContract":{"version":1,"inputs":[]},"outputs":[],"dependencies":[],"runtimeRequirements":[]}
""";
var document = System.Text.Json.Nodes.JsonNode.Parse(json)!;
var kinds = model.GetType("Bpmn.Model.BpmnElementTypes")!;
foreach(var pair in new[] { (0,"StartEvent"),(1,"CallActivity"),(2,"EndEvent") })
{
 var value=(string)kinds.GetField(pair.Item2)!.GetRawConstantValue()!;
 document["definition"]!["process"]!["elements"]![pair.Item1]!["elementType"]=value;
 Console.WriteLine(pair.Item2 + ": " + value);
}
json=document.ToJsonString();
var material = JsonSerializer.Deserialize(json,core.GetType("Elsa.Workflows.Runtime.Core.Models.RuntimeModelMaterial")!,new JsonSerializerOptions(JsonSerializerDefaults.Web));
try { bpmn.GetType("Elsa.Activities.Bpmn.Internal.BpmnDirectExecutionDriver")!.GetMethod("Graph",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[material]); Console.WriteLine("ACCEPTED"); }
catch(TargetInvocationException ex) { Console.WriteLine(ex.InnerException!.GetType().FullName + ": " + ex.InnerException.Message); }
Console.WriteLine("binaryVersion: " + FileVersionInfo.GetVersionInfo(bpmn.Location).ProductVersion);
