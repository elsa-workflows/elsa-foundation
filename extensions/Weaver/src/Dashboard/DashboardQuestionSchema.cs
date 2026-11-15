namespace Elsa.Weaver.Dashboard;

// Provider-neutral wire grammar. Dataset discovery and the native validator still decide
// which fields, metric revisions and combinations the current caller may actually use.
internal static class DashboardQuestionSchema
{
    internal const string Json = """
        {
          "type":"object","required":["question"],"additionalProperties":false,
          "properties":{"question":{
            "type":"object","required":["query","visualization","parameterValues"],"additionalProperties":false,
            "properties":{
              "query":{
                "type":"object","required":["schemaVersion","dataset","form"],"additionalProperties":false,
                "properties":{
                  "schemaVersion":{"const":1},
                  "dataset":{"type":"object","required":["id","schemaRevision"],"additionalProperties":false,
                    "properties":{"id":{"type":"string"},"schemaRevision":{"type":"string"}}},
                  "form":{"enum":["aggregate","records"]},
                  "parameters":{"type":"array","items":{"type":"object","required":["name","type"],"additionalProperties":false,
                    "properties":{"name":{"type":"string"},"type":{"enum":["string","boolean","integer","decimal","instant","date"]},
                      "required":{"type":"boolean"},"nullable":{"type":"boolean"},"default":{"$ref":"#/$defs/value"}}}},
                  "filters":{"$ref":"#/$defs/predicate"},
                  "fields":{"type":"array","items":{"type":"string"}},
                  "groupBy":{"type":"array","items":{"type":"string"}},
                  "metrics":{"type":"array","items":{"type":"object","required":["alias"],"additionalProperties":false,
                    "description":"Choose a discovered metric plus revision, or a supported aggregate plus field.",
                    "properties":{"alias":{"type":"string"},"metric":{"type":"string"},"revision":{"type":"string"},
                      "aggregate":{"enum":["count","sum","average","min","max"]},"field":{"type":"string"}}}},
                  "time":{"type":"object","required":["field"],"additionalProperties":false,
                    "properties":{"field":{"type":"string"},"window":{"enum":["absolute","calendarWeekToDate","last7Days","last30Days"]},
                      "from":{"type":"string","format":"date-time"},"to":{"type":"string","format":"date-time"},
                      "timeZone":{"type":"string"},"weekStart":{"const":"monday"},"bucket":{"enum":["hour","day"]}}},
                  "comparison":{"type":"object","required":["mode"],"additionalProperties":false,
                    "properties":{"mode":{"enum":["absolute","previousCalendarWeekSameElapsed"]},
                      "from":{"type":"string","format":"date-time"},"to":{"type":"string","format":"date-time"}}},
                  "having":{"$ref":"#/$defs/having"},
                  "sort":{"type":"array","items":{"type":"object","required":["reference"],"additionalProperties":false,
                    "properties":{"reference":{"type":"string"},"direction":{"enum":["ascending","descending"]},"nulls":{"enum":["first","last"]}}}},
                  "limit":{"type":"integer","minimum":1,"maximum":200}
                }
              },
              "visualization":{"type":"object","required":["kind","y"],"additionalProperties":false,
                "properties":{"kind":{"enum":["bar","line","table","list","metric"]},"x":{"type":"string"},
                  "y":{"type":"array","items":{"type":"string"}},"label":{"type":"string"}}},
              "parameterValues":{"type":"object","additionalProperties":{"$ref":"#/$defs/value"}}
            }
          }},
          "$defs":{
            "value":{"type":"object","required":["type"],"additionalProperties":false,
              "description":"Every non-null scalar literal is a JSON string, including integer and boolean. Use type null with no literal for explicit null, or type plus parameter for a declared parameter reference.",
              "properties":{"type":{"enum":["string","boolean","integer","decimal","instant","date","null"]},
                "literal":{"type":"string"},"parameter":{"type":"string"}}},
            "predicate":{"type":"object","additionalProperties":false,
              "description":"One field/operator leaf, or one nonempty all/any branch. IN and NOT IN use values, an array of typed value objects; other scalar operators use value. Null checks omit operands.",
              "properties":{"field":{"type":"string"},
                "operator":{"enum":["eq","ne","gt","gte","lt","lte","in","notIn","contains","startsWith","isNull","isNotNull"]},
                "value":{"$ref":"#/$defs/value"},"values":{"type":"array","items":{"$ref":"#/$defs/value"}},
                "all":{"type":"array","minItems":1,"items":{"$ref":"#/$defs/predicate"}},
                "any":{"type":"array","minItems":1,"items":{"$ref":"#/$defs/predicate"}}}},
            "having":{"type":"object","additionalProperties":false,
              "description":"Use metric (never field) with a current., baseline., or delta. metric alias, or one nonempty all/any branch.",
              "properties":{"metric":{"type":"string"},
                "operator":{"enum":["eq","ne","gt","gte","lt","lte","isNull","isNotNull"]},
                "value":{"$ref":"#/$defs/value"},"values":{"type":"array","items":{"$ref":"#/$defs/value"}},
                "all":{"type":"array","minItems":1,"items":{"$ref":"#/$defs/having"}},
                "any":{"type":"array","minItems":1,"items":{"$ref":"#/$defs/having"}}}}
          }
        }
        """;
}
