using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
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

        /// <summary>
        ///     The two options whose action is not a command's: a run that only printed help or the version checks for no
        ///     update.
        /// </summary>
        public HelpOption Help { get; }

        public VersionOption Version { get; }

        /// <summary>The operations the tree was built from: what a command that calls another operation looks it up in.</summary>
        public IReadOnlyList<Operation> Operations { get; }

        /// <summary>The channels the watch commands were built from: what `ops` lists them by.</summary>
        public IReadOnlyList<Channel> Channels { get; }

        /// <summary>The watch commands, which take `--timeout` on its own where every other command takes it beside `--wait`.</summary>
        public HashSet<Command> Watches { get; } = [];

        private CliTree(Command root,
                        GlobalOptions globals,
                        BodyOptions body,
                        HelpOption help,
                        VersionOption version,
                        IReadOnlyList<Operation> operations,
                        IReadOnlyList<Channel> channels)
        {
            Operations = operations;
            Channels = channels;
            Root = root;
            Globals = globals;
            Body = body;
            Help = help;
            Version = version;
        }

        public static CliTree Build(CliServices services, IReadOnlyList<Operation>? operations = null, IReadOnlyList<Channel>? channels = null)
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

            var tree = new CliTree(root,
                                   globals,
                                   body,
                                   help,
                                   version,
                                   operations ?? Derived.Operations.All,
                                   channels ?? Derived.Channels.All);
            DerivedCommands.AddTo(tree, services, tree.Operations);
            WatchCommands.AddTo(tree, services, tree.Channels);
            help.Action = new TreeHelpAction(tree, (HelpAction)help.Action!);
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

        /// <summary>
        ///     Help as System.CommandLine writes it, but with `--timeout` described as what it does on a watch where the
        ///     command is one: the option is one recursive global, and the library keeps its per-command customisation
        ///     internal, so the watch's description stands in for the length of that one help.
        /// </summary>
        private sealed class TreeHelpAction(CliTree tree, HelpAction help) : SynchronousCommandLineAction
        {
            /// <summary>As the wrapped help's: `--help` on a command missing a required option still shows help.</summary>
            public override bool ClearsParseErrors
            {
                get => help.ClearsParseErrors;
            }

            public override int Invoke(ParseResult parseResult)
            {
                if (!tree.Watches.Contains(parseResult.CommandResult.Command))
                {
                    return help.Invoke(parseResult);
                }

                var timeout = tree.Globals.Timeout;
                var description = timeout.Description;
                timeout.Description = GlobalOptions.WatchTimeoutDescription;
                try
                {
                    return help.Invoke(parseResult);
                }
                finally
                {
                    timeout.Description = description;
                }
            }
        }
    }
}