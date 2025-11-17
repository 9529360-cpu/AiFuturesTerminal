namespace AiFuturesTerminal.Core.Logging;

using System;
using System.IO;

/// <summary>
/// 非线程安全的简单文件记录器，用于将文本追加到指定文件。
/// 生产环境请使用成熟的日志框架（Serilog / NLog / Microsoft.Extensions.Logging）。
/// </summary>
public sealed class SimpleFileLogger
{
    private readonly string _logFilePath;
    private readonly object _syncRoot = new();

    public SimpleFileLogger(string logFilePath)
    {
        _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));
    }

    public void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}" + Environment.NewLine;
            lock (_syncRoot)
            {
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch (Exception)
        {
            // swallow IO exceptions to avoid crashing the app; in real app surface/log properly
        }
    }
}
