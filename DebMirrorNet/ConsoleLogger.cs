using Microsoft.Extensions.Logging;
using System;

namespace DebMirrorNet
{

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class CustomLoggerProvider : ILoggerProvider
    {
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName)
        {
            return new CustomConsoleLogger(categoryName);
        }

        public class CustomConsoleLogger : ILogger
        {
            private object _lock = new object();

            public CustomConsoleLogger(string categoryName)
            {
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }
                lock (_lock)
                {
                    PrintLevel(logLevel);
                    Console.WriteLine($"{formatter(state, exception)}");
                }
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            private void PrintLevel(LogLevel l)
            {
                Action<string, ConsoleColor?, ConsoleColor?> fnWriteTag = (msg, fg, bg) =>
                {
                    if (fg.HasValue)
                    {
                        Console.ForegroundColor = fg.Value;
                    }
                    if(bg.HasValue)
                    {
                        Console.BackgroundColor = bg.Value;
                    }
                    Console.Write(msg);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.Black;
                };
                switch (l)
                {
                    case LogLevel.Information:
                        {
                            fnWriteTag("info:  ", ConsoleColor.Green, null);
                            break;
                        }
                    case LogLevel.Warning:
                        {
                            fnWriteTag("warn:  ", ConsoleColor.DarkYellow, null);
                            break;
                        }
                    case LogLevel.Debug:
                        {
                            fnWriteTag("debug: ", ConsoleColor.DarkBlue, null);
                            break;
                        }
                    case LogLevel.Error:
                    case LogLevel.Critical:
                        {
                            fnWriteTag("error: ", ConsoleColor.DarkRed, null);
                            break;
                        }
                    default:
                        {
                            Console.Write($"{l.ToString().ToLower()}: ");
                            break;
                        }
                }
            }
        }
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
