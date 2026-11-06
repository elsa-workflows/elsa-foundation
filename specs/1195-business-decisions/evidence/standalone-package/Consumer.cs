using System.Text.Json;
using System.Xml.Linq;
using Elsa.Decisions;
using Elsa.Decisions.Core;
var engine = new DecisionEngine();
var assertions = 0;
void Check(bool condition,string description) { if(!condition) throw new Exception(description); assertions++; }
XNamespace ns="https://www.omg.org/spec/DMN/20191111/MODEL/";
string Xml(string[] conditions,string[] categories,string[] outputs,string outputType="string") => new XElement(ns+"definitions",new XAttribute("id","defs"),new XAttribute("namespace","urn:package-proof"),new XElement(ns+"decision",new XAttribute("id","approval"),new XElement(ns+"decisionTable",new XAttribute("id","table"),new XAttribute("hitPolicy","UNIQUE"),new XElement(ns+"input",new XAttribute("id","amount_input"),new XElement(ns+"inputExpression",new XAttribute("id","amount_expression"),new XAttribute("typeRef","number"),new XElement(ns+"text","amount"))),new XElement(ns+"input",new XAttribute("id","category_input"),new XElement(ns+"inputExpression",new XAttribute("id","category_expression"),new XAttribute("typeRef","string"),new XElement(ns+"text","category"))),new XElement(ns+"output",new XAttribute("id","output"),new XAttribute("name","approval level"),new XAttribute("typeRef",outputType)),conditions.Select((c,i)=>new XElement(ns+"rule",new XAttribute("id","r"+i),new XElement(ns+"inputEntry",new XAttribute("id","amount_test"+i),new XElement(ns+"text",c)),new XElement(ns+"inputEntry",new XAttribute("id","category_test"+i),new XElement(ns+"text",categories[i])),new XElement(ns+"outputEntry",new XAttribute("id","result"+i),new XElement(ns+"text",outputs[i]))))))).ToString();
DecisionArtifact Compile(string xml) { var compiled=engine.Compile(xml); Check(compiled.Success,"compile:"+string.Join(',',compiled.Diagnostics.Select(d=>d.Code))); return compiled.Artifact!; }
Dictionary<string,DecisionValue> Inputs(string amount,string category)=>new() { ["amount"]=new("number",amount),["category"]=new("string",category) };
var artifact=Compile(Xml(["<= 1000", "]1000..10000]", "> 10000", "> 10000"],["-","-","\"Standard\"","\"Strategic\""],["\"Automatic\"","\"Manager\"","\"Finance\"","\"Account owner\""]));
foreach(var (amount,category,expected,rule) in new []{("1000","Standard","Automatic","r0"),("1000.01","Standard","Manager","r1"),("10000","Strategic","Manager","r1"),("10000.01","Standard","Finance","r2"),("10000.01","Strategic","Account owner","r3")}) {
 var result=engine.Evaluate(artifact,Inputs(amount,category));
 Check(result.Status=="Success","evaluation status"); Check(result.Result.GetProperty("value").GetString()==expected,"approval output");
 Check(result.Trace.Single(r=>r.Selected).RuleId==rule,"actual selected rule");Check(result.Trace.Count(r=>r.Matched)==1,"actual matches");Check(result.ContentHash==artifact.ContentHash,"content identity");
}
var copied=JsonSerializer.Deserialize<DecisionArtifact>(JsonSerializer.Serialize(artifact))!;
Check(engine.Evaluate(copied,Inputs("1000","Standard")).Status=="Success","portable artifact");
var resultCopy=JsonSerializer.Deserialize<DecisionEvaluation>(JsonSerializer.Serialize(engine.Evaluate(copied,Inputs("1000","Standard"))))!;
Check(resultCopy.Trace.Single(r=>r.Selected).RuleId=="r0","portable actual explanation");
foreach(var number in new[]{"9007199254740993","1234567890123456789012345678901234","1000000000000000000000000000000000"}) {
 var exact=Compile(Xml([number],["-"],[number],"number"));
 var result=engine.Evaluate(exact,Inputs(number,"Standard"));
 Check(result.Status=="Success","exact status");Check(result.Result.GetProperty("value").GetString()==number,"exact numeric bytes");
}
Check(engine.Evaluate(artifact,Inputs("10000.01","Other")).Status=="NoMatch","no-match distinct");
Check(engine.Evaluate(artifact,Inputs("1000","Standard"),1).Status=="BudgetExceeded","bounded evaluation");
var tagged = JsonSerializer.Deserialize<DecisionValue>("{\"kind\":\"number\",\"value\":\"9007199254740993\"}")!;
Check(tagged.Kind=="number" && tagged.Value=="9007199254740993","default object materialization honors tagged wire names");
var source = Xml(["-"],["-"],["\"Approved\""]);
var normal = Compile(source);
var signed = Compile("\uFEFF"+source);
Check(signed.ContentHash==normal.ContentHash && signed.SourceHash!=normal.SourceHash,"BOM preserves original identity and executable meaning");
var unnamedDocument=XDocument.Parse(source); unnamedDocument.Descendants(ns+"output").Single().Attribute("name")!.Remove();
var unnamed=Compile(unnamedDocument.ToString());
Check(unnamed.ContentHash==normal.ContentHash,"unnamed single-output executable equivalence");
Check(engine.Evaluate(unnamed,Inputs("1","Standard")).Result.GetProperty("value").GetString()=="Approved","unnamed single-output scalar result");
Console.WriteLine(JsonSerializer.Serialize(new{sourceCommit="d9238f05e206dc0cda76afe2dbda0cdb7cad833b",assertions,status="passed",artifact.ContentHash,packageAssembly=typeof(DecisionEngine).Assembly.Location}));
