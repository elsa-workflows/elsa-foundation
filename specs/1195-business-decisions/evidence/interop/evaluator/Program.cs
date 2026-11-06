using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Elsa.Decisions;
using Elsa.Decisions.Core;
using ScratchyDisk.DmnEngine.Engine.Execution.Context;
using ScratchyDisk.DmnEngine.Parser;

NLog.LogManager.Configuration = new NLog.Config.LoggingConfiguration();
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "..");
var cases = JsonNode.Parse(File.ReadAllText(Path.Combine(root,"cases.json")))!.AsArray();
var engine = new DecisionEngine();
var reports = new List<object>();
var failures = 0;
foreach(var item in cases) {
 var c = item!;
 var xml=File.ReadAllText(Path.Combine(root,c["file"]!.GetValue<string>()));
 var inputs = c["inputs"]!.AsObject().ToDictionary(x=>x.Key,x=>new DecisionValue(x.Value!["kind"]!.GetValue<string>(),x.Value["value"]?.GetValue<string>()));
 var compiled = engine.Compile(xml);
 var evaluation = compiled.Artifact is {} artifact ? engine.Evaluate(artifact,inputs) : null;
 var actualStatus = evaluation?.Status ?? "CompilationRefused";
 var actualResult = evaluation is null || evaluation.Result.ValueKind==JsonValueKind.Undefined ? null : JsonNode.Parse(evaluation.Result.GetRawText());
 var expectedStatus = c["expectedStatus"]!.GetValue<string>();
 var statusMatches=actualStatus==expectedStatus;
 var valueMatches=JsonNode.DeepEquals(c["expectedResult"],actualResult);
 var passes=statusMatches && valueMatches;
 if(!passes)failures++;
 JsonNode? referenceResult=null;
 string referenceStatus="NotExecuted";
 string? referenceError=null;
 object? rawReference=null;
 string? referenceErrorType=null;
 try {
  var context = DmnExecutionContextFactory.CreateExecutionContext(DmnParser.ParseString13(xml),o=>o.RecordSnapshots=true);
  foreach(var (key,value) in inputs) {
   // Exact input adapter: never narrow an arbitrary-precision input silently to decimal/double.
   object? input=value.Kind switch {"null"=>null,"string"=>value.Value,"boolean"=>value.Value=="true","number"=>decimal.Parse(value.Value!,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture),_=>throw new InvalidOperationException("Unknown input kind")};
   context.WithInputParameter(key,input!);
  }
  var definition=XDocument.Parse(xml);
  var ns=definition.Root!.Name.Namespace;
  var decision=definition.Root.Element(ns+"decision")!;
  var table=decision.Element(ns+"decisionTable")!;
  var policy=(string?)table.Attribute("hitPolicy")??"UNIQUE";
  var result=context.ExecuteDecision((string?)decision.Attribute("name")??(string)decision.Attribute("id")!);
  referenceStatus=result.HasResult?"Success":"NoMatch";
  rawReference=result.Results.Select(row=>new {variables=row.Variables.Select(v=>new {v.Name,v.Value,clrType=v.Value?.GetType().FullName}),hitRules=row.HitRules.Select(r=>r.Name)}).ToArray();
  var rows=result.Results.Select(row=>row.Variables.Count==1 ? Tag(row.Variables[0].Value) : JsonSerializer.SerializeToNode(row.Variables.ToDictionary(v=>v.Name,v=>Tag(v.Value)))).ToArray();
  // Explicit adapter only unwraps reference result-variable wrappers. Preserve native no-match [] discrepancy.
  referenceResult=!result.HasResult?new JsonArray():policy is "RULE ORDER" or "COLLECT"?new JsonArray(rows):rows[0];
 }
 catch(Exception e) {
  referenceError=e.Message;referenceErrorType=e.GetType().FullName;
  referenceStatus=e.Message.Contains("hit policy",StringComparison.OrdinalIgnoreCase)&&e.Message.Contains("violation",StringComparison.OrdinalIgnoreCase)?"HitPolicyViolation":"ReferenceFailure";
 }
 var comparableStatus=actualStatus==referenceStatus;
 var valueEquivalent=JsonNode.DeepEquals(actualResult,referenceResult);
 // COLLECT allows arbitrary row order. Compare multiplicities, including duplicate rows, when both are arrays.
 if(actualResult is JsonArray left && referenceResult is JsonArray right && xml.Contains("hitPolicy=\"COLLECT\"")) valueEquivalent=left.Select(n=>n!.ToJsonString()).Order().SequenceEqual(right.Select(n=>n!.ToJsonString()).Order());
 reports.Add(new {id=c["id"]!.GetValue<string>(),file=c["file"]!.GetValue<string>(),sourceSha256=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml))),expectedStatus,expectedResult=c["expectedResult"],normative=c["normative"]!.GetValue<string>(),elsa=new {status=actualStatus,result=actualResult,diagnostics=compiled.Diagnostics.Concat(evaluation?.Diagnostics??[]).Select(d=>d.Code),trace=evaluation?.Trace,contentHash=compiled.Artifact?.ContentHash},elsaPasses=passes,reference=new {status=referenceStatus,result=referenceResult,raw=rawReference,errorType=referenceErrorType,error=referenceError},differential=new {statusEquivalent=comparableStatus,resultEquivalent=valueEquivalent,equivalent=comparableStatus&&valueEquivalent}});
}
var options=new JsonSerializerOptions {WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
File.WriteAllText(Path.Combine(root,"differential-results.json"),JsonSerializer.Serialize(new {cases=cases.Count,elsaFailures=failures,referencePackage="ScratchyDisk.DmnEngine/2.0.1",referenceSourceCommit="67bae6927a28f77138aae98dcd35d93b1af39838",profile="elsa-dmn-table-v1",semantics="elsa-dmn-table-v1.0",adapter="Typed inputs map exactly into CLR decimal/string/bool/null. Reference variable wrappers unwrap only. Native empty-list no-match and normalized output names remain visible. COLLECT order compared as multiset.",reports},options)+"\n");
Console.WriteLine($"Elsa normative expectations: {cases.Count-failures}/{cases.Count} passed. See differential-results.json for actual reference outcomes and limits.");
// Actual Studio editor output is compiled and evaluated, independently of DOM-shape assertions.
var editorCases = new[] {
 new { File = "declaredRenamed.dmn", Inputs = "{\"total\":{\"kind\":\"number\",\"value\":\"900\"}}", Expected = "{\"kind\":\"string\",\"value\":\"Approved\"}" },
 new { File = "declaredRetyped.dmn", Inputs = "{\"total\":{\"kind\":\"string\",\"value\":\"ordinary\"}}", Expected = "{\"kind\":\"string\",\"value\":\"Approved\"}" },
 new { File = "declaredOutputs.dmn", Inputs = "{\"amount\":{\"kind\":\"number\",\"value\":\"900\"}}", Expected = "{\"result\":{\"kind\":\"string\",\"value\":\"Approved\"},\"result2\":{\"kind\":\"string\",\"value\":\"\"}}" },
 new { File = "declaredScalar.dmn", Inputs = "{\"amount\":{\"kind\":\"number\",\"value\":\"900\"}}", Expected = "{\"kind\":\"number\",\"value\":\"12\"}" },
 new { File = "uniqueInput.dmn", Inputs = "{\"input2\":{\"kind\":\"number\",\"value\":\"900\"},\"input3\":{\"kind\":\"string\",\"value\":\"ordinary\"}}", Expected = "{\"kind\":\"string\",\"value\":\"Approved\"}" }
};
var editorReports = new List<object>();
foreach (var c in editorCases) {
 var bytes = File.ReadAllBytes(Path.Combine(root,"roundtrip",c.File));
 var compiled = engine.Compile(Encoding.UTF8.GetString(bytes));
 var inputs = JsonSerializer.Deserialize<Dictionary<string,DecisionValue>>(c.Inputs,new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
 var evaluated = compiled.Artifact is {} artifact ? engine.Evaluate(artifact,inputs) : null;
 var passed = evaluated?.Status == "Success" && JsonNode.DeepEquals(JsonNode.Parse(c.Expected),evaluated is null ? null : JsonNode.Parse(evaluated.Result.GetRawText()));
 if (!passed) failures++;
 editorReports.Add(new {file=c.File,sourceSha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),passed,status=evaluated?.Status,diagnostics=compiled.Diagnostics.Select(d=>d.Code),result=evaluated?.Result});
}
File.WriteAllText(Path.Combine(root,"editor-mutations.json"),JsonSerializer.Serialize(editorReports,options)+"\n");
Console.WriteLine($"Actual editor mutations: {editorCases.Length} checked; aggregate failures {failures}.");
return failures==0?0:1;

static JsonNode Tag(object? value) {
 if(value is null) return JsonSerializer.SerializeToNode(new {kind="null",value=(string?)null})!;
 if(value is string s)return JsonSerializer.SerializeToNode(new {kind="string",value=s})!;
 if(value is bool b)return JsonSerializer.SerializeToNode(new {kind="boolean",value=b?"true":"false"})!;
 if(value is decimal d)return JsonSerializer.SerializeToNode(new {kind="number",value=d.ToString("0.############################",CultureInfo.InvariantCulture)})!;
 if(value is double f)return JsonSerializer.SerializeToNode(new {kind="number",value=f.ToString("R",CultureInfo.InvariantCulture)})!;
 return JsonSerializer.SerializeToNode(new {kind="number",value=Convert.ToString(value,CultureInfo.InvariantCulture)})!;
}
