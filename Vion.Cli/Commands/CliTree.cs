using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.Linq;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Commands
{
    /// <summary>The root command with every option and command attached, built once per invocation from the derived table.</summary>
    internal sealed class CliTree
    {
        /// <summary>
        ///     Help wraps at this width whatever the terminal, so that the committed help snapshot is the same on every
        ///     machine.
        /// </summary>
        public const int HelpWidth = 110;

        public Command Root { get; }

        public GlobalOptions Globals { get; }

        public BodyOptions Body { get; }

        /// <summary>The two options whose action is not a command's: a run that only printed help or the version checks for no update.</summary>
        public HelpOption Help { get; }

        public VersionOption Version { get; }

        private CliTree(Command root, GlobalOptions globals, BodyOptions body, HelpOption help, VersionOption version)
        {
            Root = root;
            Globals = globals;
            Body = body;
            Help = help;
            Version = version;
        }

        public static CliTree Build(CliServices services, IReadOnlyList<Operation>? operations = null)
        {
            var globals = new GlobalOptions();
            var body = new BodyOptions();

            // A plain Command, not a RootCommand: the root's name is the usage line's first word, and a RootCommand takes it
            // from the process — `vion` at a shell, `testhost` under a test runner — where this one is always `vion`.
            var root = new Command("vion", "vion — the VION platform from the command line. Every operation of the cloud API is a command; sign in once, then call any of them.");
            var help = new HelpOption { Action = new HelpAction { MaxWidth = HelpWidth } };
            var version = new VersionOption();
            root.Options.Add(help);
            root.Options.Add(version);
            foreach (var option in globals.All)
            {
                root.Options.Add(option);
            }

            var tree = new CliTree(root, globals, body, help, version);
            DerivedCommands.AddTo(tree, services, operations ?? Operations.All);
            root.Subcommands.Add(AuthCommands.Create(tree, services));
            root.Subcommands.Add(ContextCommands.Create(tree, services));
            root.Subcommands.Add(WhoamiCommand.Create(tree, services));
            root.Subcommands.Add(CanICommand.Create(tree, services));
            root.Subcommands.Add(OpsCommand.Create(tree, services));
            root.Subcommands.Add(ApiCommand.Create(tree, services));
            root.Subcommands.Add(HelpSnapshotCommand.Create(tree, services));
            return tree;
        }

        /// <summary>Every command in help order, each with the path to type, depth-first; hidden ones left out.</summary>
        public IEnumerable<(IReadOnlyList<string> Path, Command Command)> VisibleCommands()
        {
            return Walk([], Root);
        }

        private static IEnumerable<(IReadOnlyList<string> Path, Command Command)> Walk(IReadOnlyList<string> path, Command command)
        {
            yield return (path, command);

            foreach (var child in command.Subcommands.Where(c => !c.Hidden))
            {
                foreach (var descendant in Walk([.. path, child.Name], child))
                {
                    yield return descendant;
                }
            }
        }
    }
}