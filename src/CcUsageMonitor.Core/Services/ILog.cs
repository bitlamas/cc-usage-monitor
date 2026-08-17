using System;

namespace CcUsageMonitor.Core.Services;

/// <summary>Logging sink — implementations MUST NEVER throw.</summary>
public interface ILog
{
    void Write(LogLevel level, string message);
    /// <summary>Logs `context` then the exception's type, message, and stack trace.</summary>
    void Exception(LogLevel level, string context, Exception? ex);
}

/// <summary>No-op sink — the default when no log is injected.</summary>
public sealed class NullLog : ILog
{
    public void Write(LogLevel level, string message) { }
    public void Exception(LogLevel level, string context, Exception? ex) { }
}
