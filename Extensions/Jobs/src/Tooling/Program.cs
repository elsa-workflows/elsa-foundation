// dotnet-ef uses the explicit factories; this executable never opens a product worker/store.
Console.Error.WriteLine("Jobs migration startup project. Use tools/jobs/generate-jobs-migrations.sh.");
return 2;
