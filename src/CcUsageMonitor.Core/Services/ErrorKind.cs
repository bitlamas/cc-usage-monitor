namespace CcUsageMonitor.Core.Services;

/// <summary>Error states that produce user-facing tooltip strings.</summary>
public enum ErrorKind
{
    /// <summary>Credentials file is missing or unreadable.</summary>
    NotLoggedIn,
    /// <summary>OAuth 401 survived refresh, and the Claude Code CLI is required for re-auth.</summary>
    CliRequiredForReauth,
    /// <summary>Generic network/timeout error — stale data retained.</summary>
    NetworkError
}
