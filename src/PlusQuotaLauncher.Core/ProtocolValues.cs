namespace PlusQuotaLauncher.Core;

public static class ProtocolValues
{
    // App Server uses different wire formats for thread/start and turn/start.
    public const string ThreadReadOnlySandbox = "read-only";
    public const string TurnReadOnlySandboxPolicy = "readOnly";
}
