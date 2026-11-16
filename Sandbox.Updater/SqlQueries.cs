using Sandbox.Common.Database;

namespace Sandbox.Updater;

public class SqlQueries(IConnection connection)
{
    public async Task<List<string>> GetUpgradesAlreadyApplied()
    {
        var migrationDoesNotExists = await MigrationDoesNotExists();
        if (!migrationDoesNotExists) return [];
        return await connection.QueryAll<string>("SELECT Name FROM __migration", []);
    }

    private async Task<bool> MigrationDoesNotExists()
    {
        var migrationTable = await connection.QueryAll<object>("SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__migration'", []);
        return migrationTable.Count == 1;
    }

    public async Task RunScript(string step, string sqlScript)
    {
        await connection.Execute(sqlScript, []);
        await connection.Execute(
            "INSERT INTO __migration (name, applied_at) VALUES (@name, @applied_at)",
            new Dictionary<string, object> {
                { "name", step },
                { "applied_at", DateTime.Now },
            }
        );
    }
}