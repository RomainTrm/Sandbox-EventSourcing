using System.Collections;
using Sandbox.Aggregates;
using Sandbox.Common;
using Sandbox.Common.Database;
using Sandbox.DCB.Full;
using Sandbox.DCB.Lite;

namespace Sandbox.Implementations.Tests;

public delegate Runner RunnerFactory(NotDisposableDatabaseConnection connection);

public class Runners : IEnumerable<object[]>
{
    public record TestCase(string Name, RunnerFactory Factory);
    
    private readonly List<TestCase> _testCases =
    [
        new("AggregatesRunner", cf => new AggregatesRunner(new StubConnectionFactory(cf))),
        new("DcbLiteRunner", cf => new DcbLiteRunner(new StubConnectionFactory(cf))),
        new("DcbFullRunner", cf => new DcbFullRunner(new StubConnectionFactory(cf))),
    ];

    public IEnumerator<object[]> GetEnumerator() => _testCases.Select(testCase => new object[] { testCase }).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}