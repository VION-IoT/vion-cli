using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Linq;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Commands
{
    /// <summary>A derived option and how to read its value off a parse result as the strings the wire takes.</summary>
    internal sealed record BoundParameter(Parameter Parameter, Option Option, Func<ParseResult, IReadOnlyList<string>?> Read);

    /// <summary>
    ///     The derived tree: `vion &lt;domain&gt; &lt;aggregate&gt; &lt;verb&gt;` for every operation in the table whose
    ///     domain is in the
    ///     tree, grouped as the table names them. Nothing here knows an operation by name.
    /// </summary>
    internal static class DerivedCommands
    {
        private static readonly Dictionary<string, string> DomainDescriptions = new(StringComparer.Ordinal)
                                                                                {
                                                                                    ["tenant"] =
                                                                                        "Operations of a tenant: its gateways, projects, dashboards, rules, users and more.",
                                                                                    ["integrator"] =
                                                                                        "Operations of an integrator: its tenants, gateways, libraries, templates and accounts.",
                                                                                    ["platform"] =
                                                                                        "Operations of the platform: integrators, platform accounts and observability.",
                                                                                    ["me"] =
                                                                                        "The caller's own: identity, notifications, preferences, profile.",
                                                                                };

        /// <summary>
        ///     The domains in the order help lists them: the one a user acts in most often first. Aggregates and verbs keep
        ///     the document's order.
        /// </summary>
        private static readonly string[] DomainOrder = ["tenant", "integrator", "platform", "me"];

        public static void AddTo(CliTree tree, CliServices services, IReadOnlyList<Operation> operations)
        {
            var domains = new Dictionary<string, Command>(StringComparer.Ordinal);
            var aggregates = new Dictionary<string, Command>(StringComparer.Ordinal);

            var ordered = operations.Where(o => o.InTree).OrderBy(o => Array.IndexOf(DomainOrder, o.CommandDomain) is var index and >= 0 ? index : DomainOrder.Length).ToList();
            foreach (var operation in ordered)
            {
                if (!domains.TryGetValue(operation.CommandDomain, out var domain))
                {
                    domain = new Command(operation.CommandDomain,
                                         DomainDescriptions.TryGetValue(operation.CommandDomain, out var text) ? text : $"Operations of the {operation.CommandDomain} domain.");
                    domains[operation.CommandDomain] = domain;
                    tree.Root.Subcommands.Add(domain);
                }

                var aggregateKey = operation.CommandDomain + " " + operation.CommandAggregate;
                if (!aggregates.TryGetValue(aggregateKey, out var aggregate))
                {
                    aggregate = new Command(operation.CommandAggregate, $"The {operation.CommandAggregate.Replace('-', ' ')} operations.");
                    aggregates[aggregateKey] = aggregate;
                    domain.Subcommands.Add(aggregate);
                }

                aggregate.Subcommands.Add(Create(operation, tree, services));
            }
        }

        private static Command Create(Operation operation, CliTree tree, CliServices services)
        {
            var command = new Command(operation.CommandVerb, operation.Description)
                          {
                              Hidden = operation.Hidden || operation.Deprecated,
                          };
            if (operation.Alias != null)
            {
                command.Aliases.Add(operation.Alias);
            }

            var bound = new List<BoundParameter>();
            foreach (var parameter in operation.Parameters.Where(p => p.OptionName != null))
            {
                var binding = Bind(parameter);
                command.Options.Add(binding.Option);
                bound.Add(binding);
            }

            if (operation.Body != null)
            {
                foreach (var option in operation.Body.IsMultipart ? tree.Body.ForMultipart : tree.Body.ForJson)
                {
                    command.Options.Add(option);
                }
            }

            command.SetAction((result, cancellationToken) => OperationInvoker.InvokeAsync(operation,
                                                                                          bound,
                                                                                          result,
                                                                                          tree,
                                                                                          services,
                                                                                          cancellationToken));
            return command;
        }

        /// <summary>
        ///     The option for a parameter, typed by its kind; every value reaches the wire as a string, so the typing is
        ///     validation and help.
        /// </summary>
        private static BoundParameter Bind(Parameter parameter)
        {
            var name = "--" + parameter.OptionName;
            switch (parameter.Kind)
            {
                case ParameterKind.Boolean:
                {
                    var option = new Option<bool>(name) { Description = parameter.Description };
                    return new BoundParameter(parameter, option, result => result.GetResult(option) is { Implicit: false } ? [result.GetValue(option) ? "true" : "false"] : null);
                }

                case ParameterKind.StringArray:
                {
                    var option = new Option<string[]>(name) { Description = parameter.Description, HelpName = "value", AllowMultipleArgumentsPerToken = false };
                    return new BoundParameter(parameter, option, result => result.GetValue(option) is { Length: > 0 } values ? values : null);
                }

                default:
                {
                    var option = new Option<string>(name) { Description = parameter.Description, HelpName = HelpNameOf(parameter.Kind) };
                    AddValidator(option, parameter.Kind);
                    return new BoundParameter(parameter, option, result => result.GetValue(option) is { } value ? [value] : null);
                }
            }
        }

        private static string HelpNameOf(ParameterKind kind)
        {
            return kind switch
            {
                ParameterKind.Uuid => "uuid",
                ParameterKind.Integer => "n",
                ParameterKind.DateTime => "iso-8601",
                _ => "value",
            };
        }

        /// <summary>A value the wire would refuse is refused here, as a parse error: exit 2 before any request.</summary>
        private static void AddValidator(Option<string> option, ParameterKind kind)
        {
            switch (kind)
            {
                case ParameterKind.Uuid:
                    option.Validators.Add(result =>
                                          {
                                              if (result.GetValueOrDefault<string>() is { } value && !Guid.TryParse(value, out _))
                                              {
                                                  result.AddError($"{option.Name} takes a UUID; '{value}' is not one.");
                                              }
                                          });
                    break;
                case ParameterKind.Integer:
                    option.Validators.Add(result =>
                                          {
                                              if (result.GetValueOrDefault<string>() is { } value &&
                                                  !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                              {
                                                  result.AddError($"{option.Name} takes an integer; '{value}' is not one.");
                                              }
                                          });
                    break;
                case ParameterKind.DateTime:
                    option.Validators.Add(result =>
                                          {
                                              if (result.GetValueOrDefault<string>() is { } value &&
                                                  !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                                              {
                                                  result.AddError($"{option.Name} takes an ISO 8601 date-time; '{value}' is not one.");
                                              }
                                          });
                    break;
            }
        }
    }
}