using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vion.Cli.Derive
{
    /// <summary>
    ///     Reads the OpenAPI document into the derived model. A second reader of the document on purpose: a generated
    ///     client drops the <c>x-vion-*</c> extensions, and they are what the tree, the flags and the help are built from.
    /// </summary>
    internal static class DocumentReader
    {
        private static readonly HashSet<string> Methods = new(["get", "put", "post", "delete", "patch"], StringComparer.Ordinal);

        public static DerivedDocument Read(string json, Presentation presentation)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var apiVersion = root.TryGetProperty("info", out var info) && info.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "";
            var schemas = root.TryGetProperty("components", out var components) && components.TryGetProperty("schemas", out var s) ? s : default;

            var operations = new List<DerivedOperation>();
            foreach (var path in root.GetProperty("paths").EnumerateObject())
            {
                foreach (var method in path.Value.EnumerateObject())
                {
                    if (!Methods.Contains(method.Name))
                    {
                        continue;
                    }

                    operations.Add(ReadOperation(path.Name, method.Name, method.Value, schemas, presentation));
                }
            }

            var ids = new HashSet<string>(operations.Select(operation => operation.Id), StringComparer.Ordinal);
            var unknown = presentation.OperationIds.Where(id => !ids.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                throw new DeriveException($"cli.json names '{unknown[0]}' and the document has no such operationId.");
            }

            return new DerivedDocument(sha, apiVersion, operations);
        }

        private static DerivedOperation ReadOperation(string path, string method, JsonElement operation, JsonElement schemas, Presentation presentation)
        {
            var id = operation.GetProperty("operationId").GetString() ?? throw new DeriveException($"{method.ToUpperInvariant()} {path} has no operationId.");
            if (id.Split('.') is not [var domain, var aggregate, var verb])
            {
                throw new DeriveException($"operationId '{id}' is not <domain>.<aggregate>.<verb>.");
            }

            var description = Collapse(TryString(operation, "description") ?? TryString(operation, "summary") ?? "");

            var parameters = new List<DerivedParameter>();
            if (operation.TryGetProperty("parameters", out var parameterArray))
            {
                foreach (var parameter in parameterArray.EnumerateArray())
                {
                    var location = parameter.GetProperty("in").GetString();
                    if (location is not ("path" or "query"))
                    {
                        // Header parameters (Idempotency-Key, If-Match) are the globals' business, not options.
                        continue;
                    }

                    var name = parameter.GetProperty("name").GetString() ?? "";
                    var required = parameter.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.True;
                    var (kind, enumValues) = ReadKind(parameter.TryGetProperty("schema", out var schema) ? schema : default, schemas);
                    var parameterDescription = Collapse(TryString(parameter, "description") ?? "");
                    if (enumValues.Count > 0)
                    {
                        parameterDescription = $"{parameterDescription} One of: {string.Join(", ", enumValues)}.".Trim();
                    }

                    var global = Naming.GlobalParameters.TryGetValue(name, out var g) ? g : null;
                    parameters.Add(new DerivedParameter(name,
                                                        global == null ? Naming.OptionName(name) : null,
                                                        global,
                                                        location == "path" ? ParameterLocation.Path : ParameterLocation.Query,
                                                        required,
                                                        kind,
                                                        parameterDescription));
                }
            }

            var body = operation.TryGetProperty("requestBody", out var requestBody) ? ReadBody(requestBody) : null;

            var capability = operation.TryGetProperty("x-vion-capability", out var cap) ? cap : default;

            // No default: an effect decides whether a call is confirmed, and a missing one would read as a harmless Read.
            var effect = (capability.ValueKind == JsonValueKind.Object ? TryString(capability, "effect") : null) ??
                         throw new DeriveException($"{id} carries no x-vion-capability.effect.");
            var authorizationElement = operation.TryGetProperty("x-vion-authorization", out var auth) ? auth : default;
            var authorization = authorizationElement.ValueKind == JsonValueKind.Object ?
                                    new DerivedAuthorization(TryString(authorizationElement, "mode") ?? "",
                                                             authorizationElement.TryGetProperty("permissions", out var permissions) ?
                                                                 permissions.EnumerateArray().Select(p => p.GetString() ?? "").ToList() : [],
                                                             TryString(authorizationElement, "scopeType"),
                                                             TryString(authorizationElement, "scopeFrom")) : new DerivedAuthorization("", [], null, null);

            var entry = presentation.For(id);
            return new DerivedOperation(id,
                                        domain,
                                        aggregate,
                                        verb,
                                        domain,
                                        Naming.Kebab(aggregate),
                                        Naming.Kebab(verb),
                                        method.ToUpperInvariant(),
                                        path,
                                        description,
                                        parameters,
                                        body,
                                        effect,
                                        TryBool(capability, "idempotent"),
                                        TryBool(capability, "versioned"),
                                        TryBool(capability, "dryRun"),
                                        TryBool(operation, "deprecated"),
                                        authorization,
                                        entry?.Hide ?? false,
                                        entry?.Alias);
        }

        private static DerivedBody? ReadBody(JsonElement requestBody)
        {
            if (!requestBody.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var first = content.EnumerateObject().FirstOrDefault();
            if (first.Value.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            var contentType = first.Name;
            var schema = first.Value.TryGetProperty("schema", out var s) ? s : default;
            if (contentType == "multipart/form-data")
            {
                string? filePart = null;
                var textParts = new List<string>();
                foreach (var property in FormProperties(schema))
                {
                    var isFile = property.Value.TryGetProperty("$ref", out var reference) && (reference.GetString() ?? "").EndsWith("/IFormFile", StringComparison.Ordinal);
                    if (isFile && filePart == null)
                    {
                        filePart = property.Name;
                    }
                    else
                    {
                        textParts.Add(property.Name);
                    }
                }

                return new DerivedBody(contentType, null, filePart, textParts);
            }

            return new DerivedBody(contentType, SchemaName(schema), null, []);
        }

        /// <summary>The properties of a form schema, including the ones an <c>allOf</c> composition contributes.</summary>
        private static IEnumerable<JsonProperty> FormProperties(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    yield return property;
                }
            }

            if (schema.TryGetProperty("allOf", out var allOf))
            {
                foreach (var part in allOf.EnumerateArray())
                {
                    foreach (var property in FormProperties(part))
                    {
                        yield return property;
                    }
                }
            }
        }

        private static string? SchemaName(JsonElement schema)
        {
            return schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference) ? reference.GetString()?.Split('/').Last() : null;
        }

        private static (ParameterKind Kind, IReadOnlyList<string> EnumValues) ReadKind(JsonElement schema, JsonElement schemas)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return (ParameterKind.String, []);
            }

            if (schema.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()?.Split('/').Last();
                if (name != null && schemas.ValueKind == JsonValueKind.Object && schemas.TryGetProperty(name, out var referenced))
                {
                    var values = referenced.TryGetProperty("enum", out var enumElement) ?
                                     enumElement.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList() : [];
                    return (ParameterKind.String, values);
                }

                return (ParameterKind.String, []);
            }

            var type = schema.TryGetProperty("type", out var typeElement) ? FirstType(typeElement) : null;
            var format = TryString(schema, "format");
            var kind = type switch
            {
                "boolean" => ParameterKind.Boolean,
                "integer" => ParameterKind.Integer,
                "array" => ParameterKind.StringArray,
                "string" when format == "uuid" => ParameterKind.Uuid,
                "string" when format == "date-time" => ParameterKind.DateTime,
                _ => ParameterKind.String,
            };
            return (kind, []);
        }

        /// <summary>
        ///     A schema's type is a string or, for a nullable or integer-or-string value, an array; the first non-null entry
        ///     decides.
        /// </summary>
        private static string? FirstType(JsonElement type)
        {
            return type.ValueKind switch
            {
                JsonValueKind.String => type.GetString(),
                JsonValueKind.Array => type.EnumerateArray().Select(t => t.GetString()).FirstOrDefault(t => t != "null"),
                _ => null,
            };
        }

        private static string? TryString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() :
                       null;
        }

        private static bool TryBool(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
        }

        /// <summary>Help is one line per description: the document's line breaks and double spaces become one space.</summary>
        private static string Collapse(string text)
        {
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}