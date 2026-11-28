# Elsa 3 import integration tests

The test host uses SQLite for the importer, Design, publication, and runtime
artifact stores. It starts Actor Foundation against a separate SQLite file.
The tests exercise the normal migration and publishing endpoints through an
authenticated default shell. HTTP workflow cases use loopback Kestrel to
exercise the raw request target. Run and activity assertions use retained Actor
reads.

The classic test host also exercised the old stimulus endpoint, checkpoint
payloads, activity completion envelopes, and private variable frames. The
retained Event test verifies the exact starter publication and resumed run.
Classic child calls with the imported cancellation policy also belong to the
archived suite. Use the source revision that still composes those stores to replay the original suite:

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 38e42f7eeb82f4458372858e6253b77c446bff94
(
  cd "$archive/source"
  dotnet test extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj \
    -c Release -p:RestoreLockedMode=true
)
```
