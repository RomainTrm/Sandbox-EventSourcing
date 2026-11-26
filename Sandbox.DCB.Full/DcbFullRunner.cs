using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.DCB.Full;

public class DcbFullRunner(IConnectionFactory connectionFactory) : Runner(connectionFactory);
