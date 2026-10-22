using Elsa.Connections.Services;

namespace Elsa.Connections.Tests;

public sealed class InMemoryConnectionRepositoryTests() : ConnectionRepositoryContractTests(new InMemoryConnectionRepository());
