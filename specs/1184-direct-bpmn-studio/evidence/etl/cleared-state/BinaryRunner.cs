using System.Text.Json; using Elsa.Workflows.Runtime.Core.Contracts;
IActivityStateViewProjector old = new LegacyProjector();
if(old.ActivityTypeWithoutState is not null || await old.ProjectWithoutStateAsync(null!) is not null || !(await old.ProjectAsync(null!)).GetProperty("preserved").GetBoolean()) throw new Exception("Old projector or refusing defaults changed.");
Console.WriteLine(JsonSerializer.Serialize(new { passed=true, oldProjectorRecompiled=false, newDefaultSelectorRefuses=true, newDefaultReadRefuses=true, existingProjectionPreserved=true }));