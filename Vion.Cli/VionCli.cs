using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Commands;
using Vion.Cli.Infrastructure;

namespace Vion.Cli
{
    /// <summary>
    ///     The whole tool as one call: build the tree, parse, refuse a wrong invocation with exit 2, else run the command.
    ///     `Program` calls it with the real process; a test calls it with fakes.
    /// </summary>
    internal static class VionCli
    {
        public static async Task<int> RunAsync(IReadOnlyList<string> args, CliServices services, IReadOnlyList<Derived.Operation>? operations = null)
        {
            var tree = CliTree.Build(services, operations);
            var parse = tree.Root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
            if (parse.Errors.Count > 0)
            {
                var globals = tree.Globals.Read(parse);
                var command = string.Join(' ', CommandPath(parse));

                // "Required command was not provided" accompanies every unrecognized token under a group; the token's own
                // error is the one that says what went wrong.
                var errors = parse.Errors.Select(error => error.Message).ToList();
                if (errors.Count > 1)
                {
                    errors.RemoveAll(error => error.StartsWith("Required command was not provided", StringComparison.Ordinal));
                }

                var message = string.Join("; ", errors);
                Guard.Report(globals, services, $"{message} Run `vion {command}{(command.Length > 0 ? " " : "")}--help` for usage.", ExitCodes.Usage);
                return ExitCodes.Usage;
            }

            try
            {
                var exitCode = await parse.InvokeAsync(new InvocationConfiguration { Output = services.Out, Error = services.Error, EnableDefaultExceptionHandler = false });

                // After the command, so the notice is the last line a person reads; not after help, the version, or a
                // hidden command — the help snapshot runs on a terminal and must not reach the network.
                if (parse.GetResult(tree.Help) == null && parse.GetResult(tree.Version) == null && !parse.CommandResult.Command.Hidden && parse.CommandResult.Command != tree.Root)
                {
                    await UpdateCheck.RunAsync(tree.Globals.Read(parse).Json, services, CancellationToken.None);
                }

                return exitCode;
            }
            catch (OperationCanceledException)
            {
                services.Error.WriteLine("error: cancelled.");
                return ExitCodes.Failure;
            }
            catch (Exception exception) when (exception is not UsageException and not CliFailure)
            {
                services.Error.WriteLine($"error: {exception.Message}");
                return ExitCodes.Failure;
            }
        }

        private static List<string> CommandPath(ParseResult parse)
        {
            var path = new List<string>();
            for (var result = parse.CommandResult; result?.Parent is CommandResult parent; result = parent)
            {
                path.Insert(0, result.Command.Name);
            }

            return path;
        }
    }
}