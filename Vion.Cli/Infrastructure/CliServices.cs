using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace Vion.Cli.Infrastructure
{
    /// <summary>Whether the streams are terminals: the `--yes` refusal and the update notice turn on it.</summary>
    internal interface IConsoleShape
    {
        bool IsInputTerminal { get; }

        bool IsOutputTerminal { get; }

        bool IsErrorTerminal { get; }

        /// <summary>The terminal's width in columns, read only when standard output is a terminal.</summary>
        int OutputWidth { get; }
    }

    /// <summary>
    ///     Everything the tool takes from the process, composed once in <c>Program</c> from the real process and once per
    ///     test from fakes. There is no container and no static state behind it; a test that needs something these do not
    ///     give has found a missing seam.
    /// </summary>
    internal sealed record CliServices(
        HttpMessageHandler Handler,
        TextWriter Out,
        TextWriter Error,
        TextReader In,
        string HomeDirectory,
        IReadOnlyDictionary<string, string?> Environment,
        TimeProvider Time,
        IConsoleShape Console,
        Func<Uri, bool> OpenBrowser,
        string Version)
    {
        public static CliServices Real()
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                environment[(string)entry.Key] = entry.Value as string;
            }

            var home = environment.TryGetValue("VION_HOME", out var vionHome) && !string.IsNullOrEmpty(vionHome) ? vionHome :
                           Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".vion");

            var informational = typeof(CliServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = string.IsNullOrEmpty(informational) ? "0.0.0" : informational.Split('+')[0];

            // The handler and the streams live as long as the process; nothing outlives them to dispose them.
#pragma warning disable CA2000
            return new CliServices(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
                                   new StreamWriter(System.Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true },
                                   new StreamWriter(System.Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true },
                                   new StreamReader(System.Console.OpenStandardInput(), new UTF8Encoding(false)),
                                   home,
                                   environment,
                                   TimeProvider.System,
                                   new RealConsoleShape(),
                                   OpenBrowserWithShell,
                                   version);
#pragma warning restore CA2000
        }

        private static bool OpenBrowserWithShell(Uri uri)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
                return process != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class RealConsoleShape : IConsoleShape
        {
            public bool IsInputTerminal
            {
                get => !System.Console.IsInputRedirected;
            }

            public bool IsOutputTerminal
            {
                get => !System.Console.IsOutputRedirected;
            }

            public bool IsErrorTerminal
            {
                get => !System.Console.IsErrorRedirected;
            }

            public int OutputWidth
            {
                get
                {
                    try
                    {
                        return System.Console.WindowWidth > 0 ? System.Console.WindowWidth : 80;
                    }
                    catch (IOException)
                    {
                        return 80;
                    }
                }
            }
        }
    }
}