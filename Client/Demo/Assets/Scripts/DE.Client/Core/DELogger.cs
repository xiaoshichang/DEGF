using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Core
{
    public enum ClientLogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Critical = 5,
    }

    public sealed class LoggingConfig
    {
        public string RootDir { get; set; }

        public string MinLevel { get; set; } = "Info";

        public bool EnableConsole { get; set; } = true;

        public bool RotateDaily { get; set; } = true;

        public int MaxFileSizeMB { get; set; } = 64;

        public int MaxRetainedFiles { get; set; } = 10;
    }

    public static class DELogger
    {
        public delegate void LogMessageHandler(ClientLogLevel level, string formattedMessage);

        private static readonly object s_syncRoot = new object();
        private static readonly UTF8Encoding s_utf8Encoding = new UTF8Encoding(false);

        private static LoggingConfig s_loggingConfig;
        private static ClientLogLevel s_minLevel = ClientLogLevel.Info;
        private static string s_loggerName = "DEFramework";
        public static string FileName => s_loggerName;
        private static string s_logDirectory;
        public static string LogDirectory => s_logDirectory;
        private static string s_logFilePath;
        private static DateTime s_currentFileDate = DateTime.MinValue;
        private static StreamWriter s_writer;

        public static event LogMessageHandler MessageLogged;

        public static bool IsInitialized()
        {
            lock (s_syncRoot)
            {
                return s_writer != null;
            }
        }

        public static void Init(string clientId, LoggingConfig loggingConfig)
        {
            lock (s_syncRoot)
            {
                if (s_writer != null)
                {
                    return;
                }

                s_loggingConfig = loggingConfig ?? CreateDefaultConfig();
                s_loggerName = string.IsNullOrWhiteSpace(clientId) ? "Client" : clientId;
                s_minLevel = ParseLogLevel(s_loggingConfig.MinLevel);
                s_logDirectory = ResolveLogDirectory(s_loggingConfig.RootDir);
                Directory.CreateDirectory(s_logDirectory);

                OpenWriter(DateTime.Now);
            }

            Info("Logger", "initialized");
        }

        public static void Uninit()
        {
            lock (s_syncRoot)
            {
                CloseWriter();
            }
        }

        public static void Debug(string tag, string message)
        {
            Log(ClientLogLevel.Debug, tag, message);
        }

        public static void Info(string tag, string message)
        {
            Log(ClientLogLevel.Info, tag, message);
        }

        public static void Warn(string tag, string message)
        {
            Log(ClientLogLevel.Warn, tag, message);
        }

        public static void Error(string tag, string message)
        {
            Log(ClientLogLevel.Error, tag, message);
        }

        public static void Debug(string message)
        {
            Log(ClientLogLevel.Debug, string.Empty, message);
        }

        public static void Info(string message)
        {
            Log(ClientLogLevel.Info, string.Empty, message);
        }

        public static void Warn(string message)
        {
            Log(ClientLogLevel.Warn, string.Empty, message);
        }

        public static void Error(string message)
        {
            Log(ClientLogLevel.Error, string.Empty, message);
        }

        private static void Log(ClientLogLevel level, string tag, string message)
        {
            var safeTag = tag ?? string.Empty;
            var safeMessage = message ?? string.Empty;

            lock (s_syncRoot)
            {
                if (s_writer == null)
                {
                    WriteUnityFallback(level, safeTag, safeMessage, true);
                    return;
                }

                if (level < s_minLevel)
                {
                    return;
                }

                var now = DateTime.Now;
                RotateIfNeeded(now);

                var formattedMessage = FormatMessage(now, level, safeTag, safeMessage);
                NotifyMessageLogged(level, formattedMessage);

                if (s_loggingConfig.EnableConsole)
                {
                    WriteUnityLog(level, formattedMessage);
                }

                s_writer.WriteLine(formattedMessage);
                s_writer.Flush();
            }
        }

        private static LoggingConfig CreateDefaultConfig()
        {
            return new LoggingConfig
            {
                RootDir = Path.Combine(UnityEngine.Application.persistentDataPath, "Logs"),
            };
        }

        private static string ResolveLogDirectory(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                return Path.Combine(UnityEngine.Application.persistentDataPath, "Logs");
            }

            if (Path.IsPathRooted(rootDir))
            {
                return rootDir;
            }

            return Path.GetFullPath(Path.Combine(UnityEngine.Application.persistentDataPath, rootDir));
        }

        private static ClientLogLevel ParseLogLevel(string minLevel)
        {
            if (string.IsNullOrWhiteSpace(minLevel))
            {
                return ClientLogLevel.Info;
            }

            switch (minLevel.Trim())
            {
                case "Trace":
                    return ClientLogLevel.Trace;
                case "Debug":
                    return ClientLogLevel.Debug;
                case "Info":
                    return ClientLogLevel.Info;
                case "Warn":
                case "Warning":
                    return ClientLogLevel.Warn;
                case "Error":
                    return ClientLogLevel.Error;
                case "Critical":
                    return ClientLogLevel.Critical;
                default:
                    return ClientLogLevel.Info;
            }
        }

        private static void RotateIfNeeded(DateTime now)
        {
            if (s_loggingConfig.RotateDaily)
            {
                if (s_currentFileDate.Date != now.Date)
                {
                    RotateCurrentFile(now);
                    CleanupRetainedFiles();
                }

                return;
            }

            var maxFileSizeBytes = Math.Max(s_loggingConfig.MaxFileSizeMB, 1) * 1024L * 1024L;
            if (s_writer.BaseStream.Length < maxFileSizeBytes)
            {
                return;
            }

            RotateBySize(now);
            CleanupRetainedFiles();
        }

        private static void RotateBySize(DateTime now)
        {
            RotateCurrentFile(now);
        }

        private static void OpenWriter(DateTime now)
        {
            s_currentFileDate = now.Date;
            s_logFilePath = Path.Combine(s_logDirectory, s_loggerName + ".log");

            var fileStream = new FileStream(s_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            s_writer = new StreamWriter(fileStream, s_utf8Encoding)
            {
                AutoFlush = true,
            };
        }

        private static void CloseWriter()
        {
            if (s_writer == null)
            {
                return;
            }

            s_writer.Flush();
            s_writer.Dispose();
            s_writer = null;
        }

        private static void RotateCurrentFile(DateTime now)
        {
            CloseWriter();

            if (File.Exists(s_logFilePath))
            {
                var rotatedPath = BuildRotatedFilePath(now);
                File.Move(s_logFilePath, rotatedPath);
            }

            OpenWriter(now);
        }

        private static string BuildRotatedFilePath(DateTime now)
        {
            var sequence = 0;
            string rotatedPath;

            do
            {
                var rotatedName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1:yyyyMMdd_HHmmss}_{2:D5}.log",
                    s_loggerName,
                    now,
                    sequence);
                rotatedPath = Path.Combine(s_logDirectory, rotatedName);
                sequence++;
            } while (File.Exists(rotatedPath));

            return rotatedPath;
        }

        private static void CleanupRetainedFiles()
        {
            var maxRetainedFiles = Math.Max(s_loggingConfig.MaxRetainedFiles, 1);
            var rotatedFiles = new DirectoryInfo(s_logDirectory)
                .GetFiles(s_loggerName + "_*.log", SearchOption.TopDirectoryOnly);

            Array.Sort(
                rotatedFiles,
                (left, right) => right.CreationTimeUtc.CompareTo(left.CreationTimeUtc));

            for (var i = maxRetainedFiles; i < rotatedFiles.Length; i++)
            {
                rotatedFiles[i].Delete();
            }
        }

        private static string FormatMessage(DateTime now, ClientLogLevel level, string tag, string message)
        {
            var threadId = Environment.CurrentManagedThreadId;
            var levelName = ToLevelName(level);

            if (string.IsNullOrEmpty(tag))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] {3}",
                    now,
                    threadId,
                    levelName,
                    message);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] [{3}] {4}",
                now,
                threadId,
                levelName,
                tag,
                message);
        }

        private static string ToLevelName(ClientLogLevel level)
        {
            switch (level)
            {
                case ClientLogLevel.Trace:
                    return "trace";
                case ClientLogLevel.Debug:
                    return "debug";
                case ClientLogLevel.Info:
                    return "info";
                case ClientLogLevel.Warn:
                    return "warning";
                case ClientLogLevel.Error:
                    return "error";
                case ClientLogLevel.Critical:
                    return "fatal";
                default:
                    return "info";
            }
        }

        private static void WriteUnityFallback(ClientLogLevel level, string tag, string message, bool uninitialized)
        {
            if (level < s_minLevel)
            {
                return;
            }

            var prefix = uninitialized ? "[UninitializedLogger] " : string.Empty;
            var levelName = ToLevelName(level);
            var formattedMessage = string.IsNullOrEmpty(tag)
                ? prefix + "[" + levelName + "] " + message
                : prefix + "[" + levelName + "] [" + tag + "] " + message;

            NotifyMessageLogged(level, formattedMessage);
            WriteUnityLog(level, formattedMessage);
        }

        private static void NotifyMessageLogged(ClientLogLevel level, string formattedMessage)
        {
            var handler = MessageLogged;
            if (handler == null)
            {
                return;
            }

            handler(level, formattedMessage);
        }

        private static void WriteUnityLog(ClientLogLevel level, string message)
        {
            switch (level)
            {
                case ClientLogLevel.Trace:
                case ClientLogLevel.Debug:
                case ClientLogLevel.Info:
                    UnityEngine.Debug.Log(message);
                    break;
                case ClientLogLevel.Warn:
                    UnityEngine.Debug.LogWarning(message);
                    break;
                case ClientLogLevel.Error:
                case ClientLogLevel.Critical:
                    UnityEngine.Debug.LogError(message);
                    break;
            }
        }
    }
}
