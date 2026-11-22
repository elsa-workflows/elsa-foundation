using System.Text.Json;

namespace Elsa.Cli.Fixtures.WorkerContextHost;

public static class FakeContextHost
{
    private const string RefusalResponse = """
        {"version":2,"status":"error","exitCode":2,"command":"list",
         "error":{"code":"host-refused","message":"The fixture refused the operation."}}
        """;

    public static TaskCompletionSource OperationEntered { get; private set; } = NewSignal();
    public static int ContextsCreated;
    public static int OperationsInvoked;
    public static int ContextsDisposed;
    public static bool ReturnRefusal;
    public static bool ReturnMalformedResponse;
    public static bool FactoryReceivedExpectedToken;
    public static bool OperationReceivedExpectedToken;
    public static CancellationToken ExpectedCancellationToken;
    public static int OperationVersion;
    public static string? OperationCommand;

    public static void Reset()
    {
        OperationEntered = NewSignal();
        ContextsCreated = 0;
        OperationsInvoked = 0;
        ContextsDisposed = 0;
        ReturnRefusal = false;
        ReturnMalformedResponse = false;
        FactoryReceivedExpectedToken = false;
        OperationReceivedExpectedToken = false;
        ExpectedCancellationToken = default;
        OperationVersion = 0;
        OperationCommand = null;
    }

    public static Elsa.Persistence.EntityFramework.Tooling.EfToolingConfigurationContext CreateConfigurationContext(
        Stream request,
        CancellationToken cancellationToken)
    {
        using var descriptor = JsonDocument.Parse(request);
        if (descriptor.RootElement.GetProperty("contextVersion").GetInt32() != 1)
            throw new InvalidDataException("Unexpected context version.");
        FactoryReceivedExpectedToken = cancellationToken == ExpectedCancellationToken;
        Interlocked.Increment(ref ContextsCreated);
        return new();
    }

    public static async Task<int> RunAsync(
        Stream request,
        Stream response,
        Elsa.Persistence.EntityFramework.Tooling.EfToolingConfigurationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref OperationsInvoked);
        using var operation = await JsonDocument.ParseAsync(request, cancellationToken: cancellationToken);
        OperationVersion = operation.RootElement.GetProperty("version").GetInt32();
        OperationCommand = operation.RootElement.GetProperty("command").GetString();
        OperationReceivedExpectedToken = cancellationToken == ExpectedCancellationToken;
        OperationEntered.TrySetResult();

        if (!ReturnRefusal && !ReturnMalformedResponse)
            await Task.Delay(Timeout.Infinite, cancellationToken);

        if (ReturnMalformedResponse)
        {
            await response.WriteAsync("{\"version\":2,"u8.ToArray(), cancellationToken);
            return 0;
        }

        await response.WriteAsync(System.Text.Encoding.UTF8.GetBytes(RefusalResponse), cancellationToken);
        return 2;
    }

    public static Task<int> LegacyRunAsync(Stream request, Stream response) => Task.FromResult(0);
    public static string ProviderPackageId(string provider) => provider;
    public static string? DescribeBindingFailure(string provider) => null;
    public static T Select<T>(string provider, string owner, T sqlite, T sqlServer, T postgreSql, T mySql) => sqlite;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
