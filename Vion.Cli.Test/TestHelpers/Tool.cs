using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>What one run of the tool produced: the exit code and both streams, whole.</summary>
    internal sealed record CliRun(int ExitCode, string Out, string Error);

    internal sealed class FakeConsoleShape : IConsoleShape
    {
        public FakeConsoleShape(bool isInputTerminal, bool isErrorTerminal)
        {
            IsInputTerminal = isInputTerminal;
            IsErrorTerminal = isErrorTerminal;
        }

        public bool IsInputTerminal { get; }

        public bool IsOutputTerminal
        {
            get => false;
        }

        public bool IsErrorTerminal { get; }

        public int OutputWidth
        {
            get => 80;
        }
    }

    /// <summary>Runs the tool in-process through the one composition root, with fakes behind every seam.</summary>
    internal static class Tool
    {
        public const string Tenant = "11111111-1111-1111-1111-111111111111";

        public const string Id = "22222222-2222-2222-2222-222222222222";

        public const string Token = "test-token";

        public static async Task<CliRun> RunAsync(HttpMessageHandler handler,
                                                  IReadOnlyList<string> args,
                                                  string? stdin = null,
                                                  bool inputTerminal = false,
                                                  IDictionary<string, string?>? environment = null,
                                                  string? home = null,
                                                  IReadOnlyList<Operation>? operations = null,
                                                  FakeTimeProvider? time = null)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var services = new CliServices(handler,
                                           output,
                                           error,
                                           new StringReader(stdin ?? ""),
                                           home ?? Path.Combine(Path.GetTempPath(), "vion-cli-test-unused"),
                                           new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>(), StringComparer.Ordinal),
                                           time ?? new FakeTimeProvider(),
                                           new FakeConsoleShape(inputTerminal, false),
                                           _ => true,
                                           "0.0.0-test");
            var exitCode = await VionCli.RunAsync(args, services, operations);
            return new CliRun(exitCode, output.ToString(), error.ToString());
        }

        /// <summary>The arguments every call against the fake API shares: the environment and the token on standard input.</summary>
        public static string[] OnTest(params string[] args)
        {
            return [.. args, "--env", "test", "--token", "-"];
        }
    }
}