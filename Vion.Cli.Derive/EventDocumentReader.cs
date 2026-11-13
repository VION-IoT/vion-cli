using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vion.Cli.Derive
{
    /// <summary>
    ///     Reads the event document into the channels a caller can watch: every channel some row hands out, with the
    ///     options that select that row and the registration the channel asks for. The operations it names are checked
    ///     against the derived operations, so a document pair out of step fails here and not at run time.
    /// </summary>
    internal static class EventDocumentReader
    {
        public const string WatchVerb = "watch";

        private const string HandedOutBy = "x-vion-handedOutBy";

        private const string RegisteredBy = "x-vion-registeredBy";

        /// <summary>The members of a channel `ops --live` compares; the channel's address and description are not.</summary>
        private static readonly string[] ContractMembers = ["parameters", "messages", HandedOutBy, RegisteredBy];

        public static DerivedChannels Read(string json, DerivedDocument operations)
        {
            using var document = JsonDocument.Parse(json);
            var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var operationIds = new HashSet<string>(operations.Operations.Select(operation => operation.Id), StringComparer.Ordinal);

            var protocol = ReadProtocol(document.RootElement);
            var hasChannels = document.RootElement.TryGetProperty("channels", out var channelsElement) && channelsElement.ValueKind == JsonValueKind.Object;
            var eventOperations = ReadOperations(document.RootElement, hasChannels ? channelsElement : default);

            var channels = new List<DerivedChannel>();
            if (hasChannels)
            {
                foreach (var channel in channelsElement.EnumerateObject())
                {
                    if (channel.Value.TryGetProperty(HandedOutBy, out var handOuts) && handOuts.ValueKind == JsonValueKind.Array && handOuts.GetArrayLength() > 0)
                    {
                        // A watch receives what the server sends on the channel, so that operation is the watch's own.
                        var sending = eventOperations.FirstOrDefault(operation => operation.Channel == channel.Name && operation.Action == "send") ??
                                      throw new DeriveException($"channel '{channel.Name}' is handed out and no operation of the event document sends on it.");
                        channels.Add(ReadChannel(channel.Name, sending.Id, channel.Value, handOuts, operationIds));
                    }
                }
            }

            RefuseCollisions(channels, operations);
            return new DerivedChannels(sha, protocol, channels, eventOperations);
        }

        /// <summary>
        ///     The one protocol the document's servers declare. None, or two different ones, leave a watch without a protocol
        ///     to name, so either fails the derive.
        /// </summary>
        private static string ReadProtocol(JsonElement root)
        {
            var protocols = root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Object ? servers.EnumerateObject()
                                .Select(server => TryString(server.Value, "protocol"))
                                .OfType<string>()
                                .Distinct(StringComparer.Ordinal)
                                .ToList() : [];
            return protocols switch
            {
                [var only] => only,
                [] => throw new DeriveException("the event document's servers declare no protocol."),
                _ => throw new DeriveException($"the event document's servers declare more than one protocol: {string.Join(", ", protocols)}."),
            };
        }

        /// <summary>
        ///     Every operation of the event document, in document order, each with the members of its channel that a
        ///     comparison with a served document reads. The address is not among them: it names the environment.
        /// </summary>
        private static List<DerivedEventOperation> ReadOperations(JsonElement root, JsonElement channels)
        {
            var result = new List<DerivedEventOperation>();
            if (!root.TryGetProperty("operations", out var operations) || operations.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var operation in operations.EnumerateObject())
            {
                var action = TryString(operation.Value, "action") ?? throw new DeriveException($"operation '{operation.Name}' names no action.");
                var reference = operation.Value.TryGetProperty("channel", out var channelElement) ? TryString(channelElement, "$ref") : null;
                const string prefix = "#/channels/";
                if (reference == null || !reference.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new DeriveException($"operation '{operation.Name}' names no channel of the document.");
                }

                var channelId = reference[prefix.Length..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (channels.ValueKind != JsonValueKind.Object || !channels.TryGetProperty(channelId, out var channel))
                {
                    throw new DeriveException($"operation '{operation.Name}' names the channel '{channelId}', which the document lacks.");
                }

                var contract = new JsonObject();
                foreach (var member in ContractMembers)
                {
                    if (channel.TryGetProperty(member, out var value))
                    {
                        contract[member] = JsonNode.Parse(value.GetRawText());
                    }
                }

                result.Add(new DerivedEventOperation(operation.Name, action, channelId, contract.ToJsonString()));
            }

            return result;
        }

        private static DerivedChannel ReadChannel(string id, string operationId, JsonElement channel, JsonElement handOutsElement, HashSet<string> operationIds)
        {
            if (id.Split('.') is not [var domain, var aggregate, _])
            {
                throw new DeriveException($"channel id '{id}' is not <domain>.<aggregate>.<kind>.");
            }

            var messageName = channel.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Object ?
                                  messages.EnumerateObject().Select(message => message.Name).FirstOrDefault() : null;
            if (messageName == null)
            {
                throw new DeriveException($"channel '{id}' has no message.");
            }

            var handOuts = handOutsElement.EnumerateArray().Select(handOut => ReadHandOut(id, handOut, operationIds)).ToList();

            var parameters = new List<DerivedChannelParameter>();
            if (channel.TryGetProperty("parameters", out var parametersElement) && parametersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var parameter in parametersElement.EnumerateObject())
                {
                    // A parameter is the caller's to supply when it picks the row: joined to a parameter of the reading
                    // operation, or to a member of a row that is one of a list. One joined to a member of the single
                    // row an operation answers picks nothing, and one no hand-out joins is left open by all of them.
                    var selecting = handOuts
                                    .Where(handOut => handOut.Raw.Any(join => join.ChannelParameter == parameter.Name && (join.OperationParameter != null || handOut.Listed)))
                                    .ToList();
                    if (selecting.Count == 0)
                    {
                        continue;
                    }

                    var description = Collapse(TryString(parameter.Value, "description") ?? "");
                    var global = Naming.GlobalParameters.TryGetValue(parameter.Name, out var key) ? key : null;
                    parameters.Add(new DerivedChannelParameter(parameter.Name,
                                                               global == null ? Naming.OptionName(parameter.Name) : null,
                                                               global,
                                                               selecting.Count == handOuts.Count,
                                                               description));
                }
            }

            var optionParameters = new HashSet<string>(parameters.Where(parameter => parameter.OptionName != null).Select(parameter => parameter.Name), StringComparer.Ordinal);
            var supplied = new HashSet<string>(parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
            var derivedHandOuts = handOuts.Select(handOut => new DerivedHandOut(handOut.Key,
                                                                                handOut.Operation,
                                                                                handOut.Row,
                                                                                handOut.Raw.Where(join => supplied.Contains(join.ChannelParameter)).ToList(),
                                                                                handOut.Raw
                                                                                       .Select(join => join.ChannelParameter)
                                                                                       .Where(optionParameters.Contains)
                                                                                       .Distinct(StringComparer.Ordinal)
                                                                                       .ToList()))
                                          .ToList();

            var registration = channel.TryGetProperty(RegisteredBy, out var registeredBy) && registeredBy.ValueKind == JsonValueKind.Object ?
                                   ReadRegistration(id, registeredBy, handOuts, parameters, operationIds) : null;

            return new DerivedChannel(id,
                                      operationId,
                                      Naming.Kebab(domain),
                                      Naming.Kebab(aggregate),
                                      Collapse(TryString(channel, "description") ?? ""),
                                      messageName,
                                      parameters,
                                      derivedHandOuts,
                                      registration);
        }

        private static RawHandOut ReadHandOut(string channelId, JsonElement handOut, HashSet<string> operationIds)
        {
            var key = TryString(handOut, "key") ?? throw new DeriveException($"channel '{channelId}': a hand-out has no key.");
            var operation = TryString(handOut, "operation") ?? throw new DeriveException($"channel '{channelId}': the hand-out under '{key}' names no operation.");
            var row = TryString(handOut, "row") ?? throw new DeriveException($"channel '{channelId}': the hand-out under '{key}' names no row.");
            if (!operationIds.Contains(operation))
            {
                throw new DeriveException($"channel '{channelId}' is handed out by a row of '{operation}' and the OpenAPI document has no such operationId.");
            }

            var joins = new List<DerivedJoin>();
            if (handOut.TryGetProperty("join", out var joinElement) && joinElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var join in joinElement.EnumerateArray())
                {
                    var parameter = TryString(join, "channelParameter") ?? throw new DeriveException($"channel '{channelId}': a join of '{operation}' names no channelParameter.");
                    var member = TryString(join, "member");
                    joins.Add(new DerivedJoin(parameter, TryString(join, "operationParameter"), member == null ? null : RelativeTo(row, member, channelId)));
                }
            }

            return new RawHandOut(key, operation, row, row.Contains("[]", StringComparison.Ordinal), joins);
        }

        private static DerivedRegistration ReadRegistration(string channelId,
                                                            JsonElement registeredBy,
                                                            List<RawHandOut> handOuts,
                                                            List<DerivedChannelParameter> parameters,
                                                            HashSet<string> operationIds)
        {
            var operation = TryString(registeredBy, "operation") ?? throw new DeriveException($"channel '{channelId}': its registration names no operation.");
            if (!operationIds.Contains(operation))
            {
                throw new DeriveException($"channel '{channelId}' is registered through '{operation}' and the OpenAPI document has no such operationId.");
            }

            var subscriberId = TryString(registeredBy, "subscriberId") ??
                               throw new DeriveException($"channel '{channelId}': its registration names no body member for the subscriber id.");

            var fills = new List<DerivedFill>();
            string? inputOption = null;
            string? inputRowMember = null;
            if (registeredBy.TryGetProperty("fill", out var fillElement) && fillElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var fill in fillElement.EnumerateArray())
                {
                    var rowMember = TryString(fill, "rowMember");
                    var bodyMember = TryString(fill, "bodyMember");
                    if (rowMember == null)
                    {
                        // A fill takes its value from what the caller supplies for the address, so one that names a
                        // parameter no hand-out joins has no value to take.
                        var channelParameter = TryString(fill, "channelParameter");
                        if (channelParameter == null || parameters.All(parameter => parameter.Name != channelParameter))
                        {
                            throw new
                                DeriveException($"channel '{channelId}': its registration fills from '{channelParameter}', which is not an address parameter a caller supplies.");
                        }

                        fills.Add(new DerivedFill(channelParameter, false, TryString(fill, "operationParameter"), bodyMember));
                        continue;
                    }

                    // One input is one option and one list in the body; two would need a rule for how their values
                    // pair up, which the document does not state.
                    if (inputOption != null || bodyMember == null)
                    {
                        throw new DeriveException($"channel '{channelId}': its registration fills more than one body member from the row, or one without a body member.");
                    }

                    if (handOuts.Count != 1)
                    {
                        throw new DeriveException($"channel '{channelId}': its registration reads a row member and the channel has {handOuts.Count} hand-outs, not one.");
                    }

                    inputRowMember = RelativeTo(handOuts[0].Row, rowMember, channelId);
                    inputOption = Naming.OptionName(bodyMember.Split('.')[^1].Replace("[]", "", StringComparison.Ordinal));
                    if (parameters.Any(parameter => parameter.OptionName == inputOption))
                    {
                        throw new DeriveException($"channel '{channelId}': its registration's option --{inputOption} is also an address parameter's.");
                    }

                    fills.Add(new DerivedFill(null, true, null, bodyMember));
                }
            }

            return new DerivedRegistration(operation, subscriberId, fills, inputOption, inputRowMember);
        }

        /// <summary>
        ///     A channel's command is its first two segments and the verb, so two channels of one aggregate, or a channel
        ///     beside an operation whose verb is the same word, would be one command with two meanings.
        /// </summary>
        private static void RefuseCollisions(List<DerivedChannel> channels, DerivedDocument operations)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var channel in channels)
            {
                var path = $"{channel.CommandDomain} {channel.CommandAggregate} {WatchVerb}";
                if (seen.TryGetValue(path, out var other))
                {
                    throw new DeriveException($"channels '{other}' and '{channel.Id}' both derive to `vion {path}`.");
                }

                seen[path] = channel.Id;
                var operation = operations.Operations.FirstOrDefault(o => o.Domain != "system" && $"{o.CommandDomain} {o.CommandAggregate} {o.CommandVerb}" == path);
                if (operation != null)
                {
                    throw new DeriveException($"channel '{channel.Id}' and operation '{operation.Id}' both derive to `vion {path}`.");
                }
            }
        }

        /// <summary>A member path as the document writes it, from the answer's root, cut down to the part below the row.</summary>
        private static string RelativeTo(string row, string member, string channelId)
        {
            var prefix = row + ".";
            if (!member.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new DeriveException($"channel '{channelId}': the member '{member}' is not below the row '{row}'.");
            }

            return member[prefix.Length..];
        }

        private static string? TryString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() :
                       null;
        }

        private static string Collapse(string text)
        {
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>A hand-out as read, before the channel's parameters decide which of its joins a caller supplies.</summary>
        private sealed record RawHandOut(string Key, string Operation, string Row, bool Listed, List<DerivedJoin> Raw);
    }
}