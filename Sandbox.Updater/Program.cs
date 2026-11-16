using Sandbox.Common.Database;
using Sandbox.Updater;

try
{
    await using var connectionFactory = new ConnectionFactory(new DatabaseConfig());
    var connection = await connectionFactory.CreateConnection();
    await using var connectionTransaction = await connection.BeginTransaction();

    var sqlQueries = new SqlQueries(connectionTransaction);
    
    var alreadyExecutedSteps = await sqlQueries.GetUpgradesAlreadyApplied();
    foreach (var step in UpgradeSteps.All.Where(step => !alreadyExecutedSteps.Contains(step)))
    {
        Console.WriteLine($"Execute {step}...");
        var sqlScript = Files.ReadScriptFile(step);
        await sqlQueries.RunScript(step, sqlScript);
    }

    await connectionTransaction.Commit();

    Console.WriteLine("Succeeded");
}
catch (Exception e)
{
    Console.WriteLine(e.ToString());
}
