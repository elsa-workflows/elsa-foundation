using Elsa.Workflows.Runtime.Native.Migration;

if (args.Contains("--migration-plan-dispatch"))
{
    try { await MigrationPlanDispatchChecks.RunAsync(); return 0; }
    catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Contains("--native-migration-validation"))
{
    var failed = false;
    await StructuredMigrationChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--native-migration-apply"))
{
    var failed = false;
    await NativeMigrationApplyChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-actor"))
{
    var failed = false;
    await MigrationPlanActorChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-runner"))
{
    var failed = false;
    await MigrationPlanRunnerChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-reads"))
{
    try { await MigrationPlanReadChecks.RunAsync(); return 0; }
    catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Contains("--retention-pass-check"))
{
    var failed = false;
    await RetentionPassChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

Console.Error.WriteLine("Choose --native-migration-validation, --native-migration-apply, --migration-plan-actor, --migration-plan-dispatch, --migration-plan-reads, --migration-plan-runner, or --retention-pass-check.");
return 2;
