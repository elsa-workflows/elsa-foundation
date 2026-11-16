# Binding retention verification

The actual pre-change host lost the candidate `elsa.direct.binding` property on XML export/import; [original evidence](../preclaim/README.md). This change retains it as an owned Elsa vendor attribute without changing the authored envelope or native child binding meaning.

| Run | Result | Meaning |
|---|---|---|
| New10 cases before production change |9failed,1passed,0skipped| Reproduced lost metadata, missing malformed/size refusal and namespace-alias overwrite; old-document control passed. |
| Same10 after change |10passed,0failed,0skipped| Exact human/service/future JSON retained; meaningful neighboring extensions still diagnosed. |
| Full suite with added depth case |142passed,1failed,0skipped| Depth refusal occurred, but test incorrectly required exact JsonException rather than derived JsonReaderException. |
| Full suite after assertion repair |143passed,0failed,0skipped|132existing cases plus11new cases, including bounded depth/refusal in both directions. |

Commands: `dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj --configuration Release`, with a class filter for the first two controls and TRX output. Runs after restore used `--no-restore`. Final exact-head command/results and downloadable raw TRXs are linked in the PR3603 checkpoint.

Author check of this increment: namespace alias conflicts are detected before translation can overwrite a value; size measures UTF-8 bytes; malformed JSON retains its original exception; only the recognized owned attribute is exempted from unrepresented-content findings. Future JSON versions are retained without being declared executable. Public signatures, runtime writer, material/request fingerprints and frozen fixture assets are untouched by this increment.

No complete PR review round has started. No direct authored publication, new claim implementation, upgraded-host/Studio acceptance or new version gate is claimed by these unit results. T006 onward remains open.
