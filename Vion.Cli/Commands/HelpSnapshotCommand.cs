using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion __help-snapshot --out &lt;file&gt;`, hidden: every visible command's `--help`, in tree order, as one file.
    ///     The
    ///     tool walks its own tree, so a new command or option is inside the snapshot by construction; `scripts/derive.ps1`
    ///     writes it to `docs/snapshots/help.txt`, the corpus anchor.
    /// </summary>
    internal static class HelpSnapshotCommand
    {
        public static Command Create(CliTree tree, CliServices services)
        {
            var output = new Option<string>("--out") { Description = "The file to write.", Required = true, HelpName = "path" };
            var command = new Command("__help-snapshot", "Write every command's help to a file.") { Hidden = true };
            command.Options.Add(output);
            command.SetAction(async (result, cancellationToken) =>
                              {
                                  var path = result.GetValue(output)!;
                                  var text = await RenderAsync(tree);
                                  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                                  await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken);
                                  services.Error.WriteLine($"wrote {path}");
                                  return ExitCodes.Ok;
                              });
            return command;
        }

        public static async Task<string> RenderAsync(CliTree tree)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var (path, _) in tree.VisibleCommands())
            {
                if (!first)
                {
                    sb.Append("---\n## vion ").Append(string.Join(' ', path)).Append('\n');
                }

                first = false;
                var writer = new StringWriter();
                var parse = tree.Root.Parse([.. path, "--help"]);
                await parse.InvokeAsync(new InvocationConfiguration { Output = writer, Error = writer });
                sb.Append(writer.ToString().Replace("\r\n", "\n", System.StringComparison.Ordinal));
            }

            return sb.ToString();
        }
    }
}