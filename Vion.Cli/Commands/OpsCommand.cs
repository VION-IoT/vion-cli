using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion ops`: the tree's operations from the committed document, offline — what the tool can call and what each
    ///     call does to the state. `--all` adds the `system` operations, which have no command and are reached through `api`.
    /// </summary>
    internal static class OpsCommand
    {
        public static Command Create(CliTree tree, CliServices services)
        {
            var all = new Option<bool>("--all") { Description = "Include the system operations, which have no command." };
            var command = new Command("ops",
                                      "List the operations of the committed document: the command, the operationId, the method and path, the effect and the write-safety flags.");
            command.Options.Add(all);
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => Task.FromResult(Run(globals, result.GetValue(all), services)));
                              });
            return command;
        }

        private static int Run(GlobalValues globals, bool includeSystem, CliServices services)
        {
            var rows = new JsonArray();
            foreach (var operation in Operations.All.Where(o => o.InTree || includeSystem))
            {
                rows.Add((JsonNode)new JsonObject
                                   {
                                       ["command"] = operation.InTree ? "vion " + operation.CommandPath : null,
                                       ["operationId"] = operation.Id,
                                       ["method"] = operation.Method,
                                       ["path"] = operation.Template,
                                       ["effect"] = operation.Effect.ToString(),
                                       ["idempotent"] = operation.Idempotent,
                                       ["versioned"] = operation.Versioned,
                                       ["dryRun"] = operation.DryRun,
                                       ["deprecated"] = operation.Deprecated,
                                       ["hidden"] = operation.Hidden,
                                   });
            }

            var document = new JsonObject { ["items"] = rows };
            if (globals.Json)
            {
                Renderer.WriteJson(Fields.Project(document, globals.Fields), services);
                return ExitCodes.Ok;
            }

            var table = new JsonArray();
            foreach (var row in rows.OfType<JsonObject>())
            {
                var flags = new List<string>();
                if ((bool)row["idempotent"]!)
                {
                    flags.Add("idempotent");
                }

                if ((bool)row["versioned"]!)
                {
                    flags.Add("versioned");
                }

                if ((bool)row["dryRun"]!)
                {
                    flags.Add("dry-run");
                }

                if ((bool)row["deprecated"]!)
                {
                    flags.Add("deprecated");
                }

                if ((bool)row["hidden"]!)
                {
                    flags.Add("hidden");
                }

                table.Add((JsonNode)new JsonObject
                                    {
                                        ["command"] = row["command"]?.DeepClone() ?? "—",
                                        ["operation"] = row["operationId"]!.DeepClone(),
                                        ["method"] = row["method"]!.DeepClone(),
                                        ["path"] = row["path"]!.DeepClone(),
                                        ["effect"] = row["effect"]!.DeepClone(),
                                        ["flags"] = string.Join(" ", flags),
                                    });
            }

            Renderer.WriteTable(new JsonObject { ["items"] = table }, globals, services);
            services.Error.WriteLine($"{rows.Count} operations");
            return ExitCodes.Ok;
        }
    }
}