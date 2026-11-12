using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     The watch commands: `vion &lt;domain&gt; &lt;aggregate&gt; watch` for every channel in the table, beside the
    ///     aggregate's operations where it has any. Nothing here knows a channel by name.
    /// </summary>
    internal static class WatchCommands
    {
        public static void AddTo(CliTree tree, CliServices services, IReadOnlyList<Channel> channels)
        {
            foreach (var channel in channels)
            {
                var domain = Group(tree.Root, channel.CommandDomain, () => DerivedCommands.DomainDescription(channel.CommandDomain));
                var aggregate = Group(domain, channel.CommandAggregate, () => DerivedCommands.AggregateDescription(channel.CommandAggregate));
                var command = Create(channel, tree, services);
                aggregate.Subcommands.Add(command);
                tree.Watches.Add(command);
            }
        }

        private static Command Create(Channel channel, CliTree tree, CliServices services)
        {
            var command = new Command(Channel.Verb, channel.Description);
            var bound = new List<(ChannelParameter Parameter, Option<string> Option)>();
            foreach (var parameter in channel.Parameters.Where(p => p.OptionName != null))
            {
                var option = new Option<string>("--" + parameter.OptionName) { Description = parameter.Description, HelpName = "value", Required = parameter.Required };
                command.Options.Add(option);
                bound.Add((parameter, option));
            }

            Option<string[]>? input = null;
            if (channel.Registration?.InputOptionName is { } inputName)
            {
                input = new Option<string[]>("--" + inputName)
                        {
                            Description =
                                $"Register this {inputName.Replace('-', ' ')} only; repeatable. Every one the row carries when absent.",
                            HelpName = "value",
                            AllowMultipleArgumentsPerToken = false,
                        };
                command.Options.Add(input);
            }

            command.SetAction((result, cancellationToken) => WatchInvoker.InvokeAsync(channel,
                                                                                      bound,
                                                                                      input,
                                                                                      result,
                                                                                      tree,
                                                                                      services,
                                                                                      cancellationToken));
            return command;
        }

        /// <summary>
        ///     The group of that name under the parent, made where the operations left none: a channel may be all an
        ///     aggregate has.
        /// </summary>
        private static Command Group(Command parent, string name, Func<string> description)
        {
            var group = parent.Subcommands.FirstOrDefault(command => command.Name == name);
            if (group == null)
            {
                group = new Command(name, description());
                parent.Subcommands.Add(group);
            }

            return group;
        }
    }
}