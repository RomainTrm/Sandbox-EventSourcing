using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.DCB.Lite;

public class DcbLiteRunner(IConnectionFactory connectionFactory) : Runner(connectionFactory);
