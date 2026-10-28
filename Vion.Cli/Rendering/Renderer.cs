using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Rendering;
using Vion.Cli.Commands;
using Vion.Cli.Derived;
using Vion.Cli.Http;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Rendering
{
    /// <summary>
    ///     How an answer reaches the user. JSON mode: exactly one document on standard output, the body or an error
    ///     object. Table mode: a list as rows, any other object as key–value pairs, through Spectre; errors and notices on
    ///     standard error. The same two paths for every command, so a rule that holds for one holds for all.
    /// </summary>
    internal static class Renderer
    {
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        public static int Render(ApiResponse response, GlobalValues globals, CliServices services, Operation? operation)
        {
            if (response.Replayed)
            {
                services.Error.WriteLine("replayed: the first answer under this idempotency key.");
            }

            if (response.IsSuccess)
            {
                if (response.Json is JsonObject envelope && envelope["nextCursor"] is JsonValue cursor && cursor.TryGetValue<string>(out var next) && !string.IsNullOrEmpty(next))
                {
                    services.Error.WriteLine($"next cursor: {next} (pass it as --cursor)");
                }

                var body = Fields.Project(response.Json, globals.Fields);
                if (globals.Json)
                {
                    WriteJson(response.Json == null && response.Text != null ? JsonValue.Create(response.Text) : body, services);
                }
                else if (response.Json != null)
                {
                    WriteTable(body, globals, services);
                }
                else if (response.Text != null)
                {
                    services.Out.WriteLine(response.Text);
                }

                return ExitCodes.Ok;
            }

            var error = ApiError.From(response, operation);
            if (globals.Json)
            {
                WriteJson(new JsonObject { ["error"] = error.ToJson() }, services);
            }
            else
            {
                foreach (var line in error.Lines())
                {
                    services.Error.WriteLine(line);
                }
            }

            return ExitCodes.Failure;
        }

        public static void WriteJson(JsonNode? node, CliServices services)
        {
            services.Out.WriteLine(node == null ? "null" : node.ToJsonString(Indented));
        }

        public static void WriteTable(JsonNode? node, GlobalValues globals, CliServices services)
        {
            var console = AnsiConsole.Create(new AnsiConsoleSettings
                                             {
                                                 Ansi = AnsiSupport.No,
                                                 ColorSystem = ColorSystemSupport.NoColors,
                                                 Interactive = InteractionSupport.No,
                                                 Out = new StreamOutput(services),
                                             });

            switch (node)
            {
                case JsonObject envelope when envelope["items"] is JsonArray items:
                    console.Write(RowTable(items, globals.Fields));
                    break;
                case JsonObject obj:
                    console.Write(KeyValueTable(obj));
                    break;
                case JsonArray array:
                    console.Write(RowTable(array, globals.Fields));
                    break;
                case null:
                    break;
                default:
                    services.Out.WriteLine(Scalar(node));
                    break;
            }
        }

        /// <summary>A string unquoted, a number or boolean as written, null empty, anything nested as compact JSON.</summary>
        public static string Scalar(JsonNode? value)
        {
            return value switch
            {
                null => "",
                JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
                _ => value.ToJsonString(),
            };
        }

        /// <summary>
        ///     The columns are the fields asked for, else every scalar member of any row in first-seen order; `meta` is an
        ///     object and so never one.
        /// </summary>
        private static Table RowTable(JsonArray items, IReadOnlyList<string> fields)
        {
            var columns = new List<string>(fields);
            if (columns.Count == 0)
            {
                foreach (var item in items.OfType<JsonObject>())
                {
                    foreach (var (name, value) in item)
                    {
                        if (value is not JsonObject and not JsonArray && !columns.Contains(name))
                        {
                            columns.Add(name);
                        }
                    }
                }
            }

            var table = new Table { Border = TableBorder.Simple };
            foreach (var column in columns)
            {
                table.AddColumn(new TableColumn(new Text(column)));
            }

            foreach (var item in items)
            {
                table.AddRow(columns.Select(column => (IRenderable)new Text(Fields.TryGet(item, column, out var value) ? Scalar(value) : "")));
            }

            if (columns.Count == 0)
            {
                table.AddColumn(new TableColumn(new Text("(no rows)")));
            }

            return table;
        }

        private static Table KeyValueTable(JsonObject obj)
        {
            var table = new Table { Border = TableBorder.Simple, ShowHeaders = false };
            table.AddColumn(new TableColumn(new Text("key")));
            table.AddColumn(new TableColumn(new Text("value")));
            foreach (var (name, value) in obj)
            {
                table.AddRow(new Text(name), new Text(Scalar(value)));
            }

            return table;
        }

        /// <summary>
        ///     Spectre's output over the tool's standard output. On a terminal the table fits the window; in a pipe or a
        ///     file it is laid out as if the line were endless, so that a cell — an operationId, a path — is never broken
        ///     across lines for a reader that is a program.
        /// </summary>
        private sealed class StreamOutput : IAnsiConsoleOutput
        {
            private const int Unwrapped = 4000;

            private readonly CliServices _services;

            public StreamOutput(CliServices services)
            {
                _services = services;
            }

            public System.IO.TextWriter Writer
            {
                get => _services.Out;
            }

            public bool IsTerminal
            {
                get => _services.Console.IsOutputTerminal;
            }

            public int Width
            {
                get => _services.Console.IsOutputTerminal ? _services.Console.OutputWidth : Unwrapped;
            }

            public int Height
            {
                get => 25;
            }

            public void SetEncoding(System.Text.Encoding encoding)
            {
            }
        }
    }
}