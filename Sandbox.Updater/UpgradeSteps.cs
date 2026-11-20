namespace Sandbox.Updater;

public static class UpgradeSteps
{
    public static List<string> All => [
        "initialize",
        "add-aggregates-event-store",
        "add-dcb-event-store",
    ];
}