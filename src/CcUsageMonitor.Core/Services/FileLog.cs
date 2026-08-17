using System;
using System.IO;
using System.Text;

namespace CcUsageMonitor.Core.Services;

/// <summary>File-based log sink with size-based rotation. Implementations MUST NEVER throw.</summary>
public sealed class FileLog : ILog
{
    private readonly string _logFilePath;
    private readonly string _rolledFilePath;
    private readonly IClock _clock;
    private readonly long _maxSizeBytes;
    private readonly object _lock = new();

    // UTF-8 WITHOUT a BOM — File.AppendAllText(Encoding.UTF8) emits an EF BB BF prefix on a
    // fresh/rotated file, which would corrupt the first line (the startup line) and any strict
    // line-prefix parse. Encode raw UTF-8 bytes only.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Creates a FileLog. Ensures the directory exists and rolls if the log exceeds maxSizeBytes.
    /// maxSizeBytes default 5_000_000 (5 MB); keeps ONE rolled backup (.log.1).
    /// </summary>
    public FileLog(string logFilePath, string rolledFilePath, long maxSizeBytes = 5_000_000, IClock? clock = null)
    {
        _logFilePath = logFilePath;
        _rolledFilePath = rolledFilePath;
        _maxSizeBytes = maxSizeBytes;
        _clock = clock ?? new SystemClock();

        // Best-effort setup — MUST NEVER throw. A logger that aborts construction would kill
        // startup (and the global handlers aren't installed yet), turning the diagnostics feature
        // into the very invisible crash it exists to eliminate. If any of this fails (locked
        // .log.1, read-only/full disk, long path), degrade to a no-op sink: Write/Exception below
        // already swallow, so the app runs on without logs rather than not at all.
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

            // Rotation is checked ONLY at construction (launch)
            if (File.Exists(logFilePath))
            {
                var currentSize = new FileInfo(logFilePath).Length;
                if (LogRotation.ShouldRoll(currentSize, maxSizeBytes))
                {
                    if (File.Exists(rolledFilePath))
                        File.Delete(rolledFilePath);
                    File.Move(logFilePath, rolledFilePath);
                }
            }
        }
        catch
        {
            // Swallow all — a logger must not crash the app.
        }
    }

    /// <summary>Append a log line. Best-effort — never throws.</summary>
    public void Write(LogLevel level, string message)
    {
        try
        {
            lock (_lock)
            {
                var line = LogFormat.Line(_clock.Now, level, message) + Environment.NewLine;
                File.AppendAllText(_logFilePath, line, Utf8NoBom);
            }
        }
        catch
        {
            // Swallow all — a logger must not crash the app.
        }
    }

    /// <summary>Log context + exception type/message, then raw stack trace. Best-effort — never throws.</summary>
    public void Exception(LogLevel level, string context, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var msg = $"{context}: {ex?.GetType().Name}: {ex?.Message}";
                var line = LogFormat.Line(_clock.Now, level, msg) + Environment.NewLine;
                File.AppendAllText(_logFilePath, line, Utf8NoBom);

                if (ex?.StackTrace is not null)
                {
                    File.AppendAllText(_logFilePath, ex.StackTrace + Environment.NewLine, Utf8NoBom);
                }
            }
        }
        catch
        {
            // Swallow all — a logger must not crash the app.
        }
    }
}
