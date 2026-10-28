using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Cli.Commands;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Http
{
    /// <summary>
    ///     The body of a call from what the user gave: `--input` as the document, `-f` pairs as strings, `-F` pairs typed,
    ///     dotted keys nesting — `gh api`'s shape. A multipart body is the file part from `--file` and the text parts from
    ///     `-f`.
    /// </summary>
    internal static class BodyInput
    {
        public static JsonNode? BuildJson(BodyValues body, CliServices services)
        {
            if (!body.Any)
            {
                return null;
            }

            JsonNode? document = null;
            if (body.Input != null)
            {
                var text = body.Input == "-" ? services.In.ReadToEnd() : File.ReadAllText(body.Input);
                try
                {
                    document = JsonNode.Parse(text);
                }
                catch (JsonException exception)
                {
                    throw new UsageException($"--input: not JSON: {exception.Message}");
                }
            }

            if (body.RawFields.Count == 0 && body.Fields.Count == 0)
            {
                return document;
            }

            if (document == null)
            {
                document = new JsonObject();
            }

            if (document is not JsonObject target)
            {
                throw new UsageException("-f and -F set members of an object, and --input gave something else.");
            }

            foreach (var pair in body.RawFields)
            {
                var (key, value) = SplitPair(pair);
                Set(target, key, JsonValue.Create(value));
            }

            foreach (var pair in body.Fields)
            {
                var (key, value) = SplitPair(pair);
                Set(target, key, Typed(value, services));
            }

            return target;
        }

        public static HttpContent JsonContent(JsonNode node)
        {
            return new StringContent(node.ToJsonString(), new UTF8Encoding(false), "application/json");
        }

        public static HttpContent BuildMultipart(Body body, BodyValues values)
        {
            if (values.File == null)
            {
                throw new UsageException($"this operation uploads a file: pass --file <path> as its '{body.FilePart}' part.");
            }

            if (!File.Exists(values.File))
            {
                throw new UsageException($"--file: '{values.File}' does not exist.");
            }

            // The parts are owned by the multipart content, which the request owns and disposes with itself.
#pragma warning disable CA2000
            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(File.OpenRead(values.File));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, body.FilePart ?? "file", Path.GetFileName(values.File));
            foreach (var pair in values.RawFields)
            {
                var (key, value) = SplitPair(pair);
                content.Add(new StringContent(value, new UTF8Encoding(false)), key);
            }
#pragma warning restore CA2000

            return content;
        }

        public static KeyValuePair<string, string> SplitPair(string pair)
        {
            var at = pair.IndexOf('=', StringComparison.Ordinal);
            if (at <= 0)
            {
                throw new UsageException($"'{pair}' is not key=value.");
            }

            return new KeyValuePair<string, string>(pair[..at], pair[(at + 1)..]);
        }

        /// <summary>
        ///     `-F`'s typing: the JSON literals and numbers as themselves, `@file` as the file's text, everything else a
        ///     string.
        /// </summary>
        private static JsonValue? Typed(string value, CliServices services)
        {
            switch (value)
            {
                case "true":
                    return JsonValue.Create(true);
                case "false":
                    return JsonValue.Create(false);
                case "null":
                    return null;
                default:
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        return JsonValue.Create(integer);
                    }

                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        return JsonValue.Create(number);
                    }

                    if (value.StartsWith('@'))
                    {
                        var path = value[1..];
                        return JsonValue.Create(path == "-" ? services.In.ReadToEnd() : File.ReadAllText(path));
                    }

                    return JsonValue.Create(value);
            }
        }

        /// <summary>A dotted key creates the objects on the way; an existing member of another shape is replaced.</summary>
        private static void Set(JsonObject target, string dottedKey, JsonNode? value)
        {
            var segments = dottedKey.Split('.');
            var current = target;
            foreach (var segment in segments.Take(segments.Length - 1))
            {
                if (current[segment] is not JsonObject next)
                {
                    next = new JsonObject();
                    current[segment] = next;
                }

                current = next;
            }

            current[segments[^1]] = value;
        }
    }
}