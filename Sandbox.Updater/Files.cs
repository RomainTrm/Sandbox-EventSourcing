using System.Reflection;

namespace Sandbox.Updater;

public static class Files
{
    public static string ReadScriptFile(string name)
    {
        var rootDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;
        return File.ReadAllText(Path.Combine(rootDirectory, "Migrations", name + ".sql"));
    }
}