using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.Aggregates;

public class AggregatesRunner(IConnectionFactory connectionFactory) : Runner(connectionFactory);
