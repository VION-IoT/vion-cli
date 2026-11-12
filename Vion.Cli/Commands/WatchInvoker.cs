using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Auth;
using Vion.Cli.Bus;
using Vion.Cli.Context;
using Vion.Cli.Derived;
using Vion.Cli.Http;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     Runs one watch command: resolves the context, reads the row that hands the channel out and takes its topic,
    ///     prepares the registration where the channel asks for one, then hands the watch to the runner and prints what
    ///     it delivers. The one path every channel takes; nothing here knows a channel by name.
    /// </summary>
    internal static class WatchInvoker
    {
        private const string SubscriberOperation = "me.subscriber.create";

        public static Task<int> InvokeAsync(Channel channel,
                                            IReadOnlyList<(ChannelParameter Parameter, Option<string> Option)> bound,
                                            Option<string[]>? input,
                                            ParseResult result,
                                            CliTree tree,
                                            CliServices services,
                                            CancellationToken cancellationToken)
        {
            var globals = tree.Globals.Read(result);
            return Guard.RunAsync(globals,
                                  services,
                                  async () =>
                                  {
                                      try
                                      {
                                          return await RunAsync(channel,
                                                                bound,
                                                                input,
                                                                result,
                                                                globals,
                                                                tree,
                                                                services,
                                                                cancellationToken);
                                      }
                                      catch (ApiAnswerException refused)
                                      {
                                          return Renderer.Render(refused.Response, globals, services, refused.Operation);
                                      }
                                  });
        }

        private static async Task<int> RunAsync(Channel channel,
                                                IReadOnlyList<(ChannelParameter Parameter, Option<string> Option)> bound,
                                                Option<string[]>? input,
                                                ParseResult result,
                                                GlobalValues globals,
                                                CliTree tree,
                                                CliServices services,
                                                CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            if (globals.DryRun)
            {
                throw new UsageException("--dry-run: a watch offers no dry run.");
            }

            if (!Uri.TryCreate(context.Environment.BrokerUrl, UriKind.Absolute, out var pinned))
            {
                throw new
                    UsageException($"the environment {context.Environment.Name} names no broker, so a watch has no host it may send the token to: define it with `vion context use --env <name> --api-url <url> --auth-url <realm url> --broker-url <wss url>`, or set VION_BROKER_URL.");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var given = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in channel.Parameters)
            {
                if (parameter.Global != null)
                {
                    values[parameter.Name] = context.Root(parameter.Global) ??
                                             throw new
                                                 UsageException($"{channel.Id} needs a {parameter.Global}: pass --{parameter.Global} <id>, or set one with `vion context use --{parameter.Global} <id>`.");
                }
                else if (result.GetValue(bound.First(b => b.Parameter == parameter).Option) is { } value)
                {
                    values[parameter.Name] = value;
                    given.Add(parameter.Name);
                }
            }

            var handOut = channel.HandOuts.FirstOrDefault(candidate => given.SetEquals(candidate.Selects)) ??
                          throw new UsageException($"{channel.Id} is handed out for {HandOutChoices(channel)}; the options given name none of these.");

            using var client = new ApiClient(services);
            var tokens = new TokenCache(globals, context, services);
            var token = await tokens.NextAsync(cancellationToken);

            var row = await ReadRowAsync(channel,
                                         handOut,
                                         values,
                                         tree.Operations,
                                         context,
                                         globals,
                                         token,
                                         client,
                                         cancellationToken);
            var topic = (row["meta"] as JsonObject)?["topics"] is JsonObject topics && topics[handOut.Key] is JsonValue handedOut && handedOut.TryGetValue<string>(out var text) ?
                            text :
                            throw new
                                CliFailure($"{channel.Id}: the row of {handOut.Operation} hands out no topic for it. The caller may read the row without being granted the channel.");

            var register = channel.Registration is { } registration ? Registration(channel,
                                                                                   registration,
                                                                                   input,
                                                                                   result,
                                                                                   row,
                                                                                   values,
                                                                                   tree.Operations,
                                                                                   context,
                                                                                   globals,
                                                                                   client) : null;

            var plan = new WatchPlan(channel,
                                     topic,
                                     globals.Timeout,
                                     tokens.NextAsync,
                                     async (current, cancel) => await CreateSubscriberAsync(pinned,
                                                                                            tree.Operations,
                                                                                            context,
                                                                                            globals,
                                                                                            current,
                                                                                            client,
                                                                                            cancel),
                                     register);
            await WatchRunner.RunAsync(plan, message => Write(message, globals, services), services, cancellationToken);
            return ExitCodes.Ok;
        }

        /// <summary>
        ///     The row the options select, in the answer of the operation the hand-out names. A join to an operation
        ///     parameter fills the request; a join to a member picks among the rows answered.
        /// </summary>
        private static async Task<JsonObject> ReadRowAsync(Channel channel,
                                                           HandOut handOut,
                                                           Dictionary<string, string> values,
                                                           IReadOnlyList<Operation> operations,
                                                           ResolvedContext context,
                                                           GlobalValues globals,
                                                           string token,
                                                           ApiClient client,
                                                           CancellationToken cancellationToken)
        {
            var operation = OperationOf(operations, handOut.Operation);
            var pathValues = handOut.Joins
                                    .Where(join => join.OperationParameter != null)
                                    .ToDictionary(join => join.OperationParameter!, join => values[join.ChannelParameter], StringComparer.Ordinal);
            var response = await SendAsync(operation,
                                           pathValues,
                                           null,
                                           context,
                                           globals,
                                           token,
                                           client,
                                           cancellationToken);

            var members = handOut.Joins.Where(join => join.Member != null).ToList();
            var row = RowPath.Select(response.Json, handOut.Row)
                             .OfType<JsonObject>()
                             .FirstOrDefault(candidate => members.All(join => RowPath.Select(candidate, join.Member!)
                                                                                     .Any(member => Same(Renderer.Scalar(member), values[join.ChannelParameter]))));
            if (row != null)
            {
                return row;
            }

            var named = string.Join(", ", members.Select(join => $"{join.Member} {values[join.ChannelParameter]}"));
            throw new CliFailure($"{channel.Id}: {handOut.Operation} answered no row{(named.Length > 0 ? $" with {named}" : "")}.");
        }

        /// <summary>
        ///     The registration as the runner calls it after each acknowledgement. Its input is checked here, before any
        ///     connection: the host drops a value it does not know without a word, and the watch would deliver nothing.
        /// </summary>
        private static Func<BrokerSession, string, CancellationToken, Task> Registration(Channel channel,
                                                                                         Registration registration,
                                                                                         Option<string[]>? input,
                                                                                         ParseResult result,
                                                                                         JsonObject row,
                                                                                         Dictionary<string, string> values,
                                                                                         IReadOnlyList<Operation> operations,
                                                                                         ResolvedContext context,
                                                                                         GlobalValues globals,
                                                                                         ApiClient client)
        {
            var operation = OperationOf(operations, registration.Operation);
            IReadOnlyList<string> chosen = [""];
            if (registration.InputRowMember != null && input != null)
            {
                var carried = RowPath.Select(row, registration.InputRowMember).Select(Renderer.Scalar).ToList();
                var asked = result.GetValue(input) ?? [];
                var unknown = asked.Where(value => !carried.Contains(value, StringComparer.Ordinal)).ToList();
                if (unknown.Count > 0)
                {
                    throw new UsageException($"--{registration.InputOptionName}: the row carries no '{unknown[0]}'. It carries: {string.Join(", ", carried)}.");
                }

                chosen = asked.Length > 0 ? asked : carried;
            }

            var pathValues = registration.Fills
                                         .Where(fill => fill is { OperationParameter: not null, ChannelParameter: not null })
                                         .ToDictionary(fill => fill.OperationParameter!, fill => values[fill.ChannelParameter!], StringComparer.Ordinal);

            var registeredOnce = false;
            return async (session, token, cancellationToken) =>
                   {
                       var body = new JsonObject { [registration.SubscriberIdMember] = session.SubscriberId };
                       foreach (var fill in registration.Fills.Where(fill => fill.BodyMember != null))
                       {
                           var separator = fill.BodyMember!.IndexOf(RowPath.ListMark + ".", StringComparison.Ordinal);
                           if (separator < 0)
                           {
                               body[fill.BodyMember] = values[fill.ChannelParameter!];
                               continue;
                           }

                           // One element per value of the input, each carrying the address parameters beside it.
                           var list = fill.BodyMember[..separator];
                           var member = fill.BodyMember[(separator + RowPath.ListMark.Length + 1)..];
                           if (body[list] is not JsonArray elements)
                           {
                               elements = new JsonArray(chosen.Select(_ => (JsonNode)new JsonObject()).ToArray());
                               body[list] = elements;
                           }

                           for (var index = 0; index < chosen.Count; index++)
                           {
                               elements[index]![member] = fill.FromInput ? chosen[index] : values[fill.ChannelParameter!];
                           }
                       }

                       try
                       {
                           await SendAsync(operation,
                                           pathValues,
                                           body,
                                           context,
                                           globals,
                                           token,
                                           client,
                                           cancellationToken);
                       }
                       catch (ApiAnswerException answer) when (registeredOnce && answer.Response.Status >= 500)
                       {
                           // The host answered the first registration, so a server error on a later one is the host
                           // on its way back up and not a refusal of the request.
                           throw new NoAnswer($"{operation.Id} answered {answer.Response.Status}.");
                       }

                       registeredOnce = true;
                   };
        }

        private static async Task<BrokerSession> CreateSubscriberAsync(Uri pinned,
                                                                       IReadOnlyList<Operation> operations,
                                                                       ResolvedContext context,
                                                                       GlobalValues globals,
                                                                       string token,
                                                                       ApiClient client,
                                                                       CancellationToken cancellationToken)
        {
            var operation = OperationOf(operations, SubscriberOperation);
            var response = await SendAsync(operation,
                                           new Dictionary<string, string>(),
                                           null,
                                           context,
                                           globals,
                                           token,
                                           client,
                                           cancellationToken);
            var answer = response.Json as JsonObject;
            var will = answer?["lastWill"] as JsonObject;
            if (Uri.TryCreate(Renderer.Scalar(answer?["brokerUrl"]), UriKind.Absolute, out var broker) &&
                Renderer.Scalar(answer?["subscriberId"]) is { Length: > 0 } subscriberId && Renderer.Scalar(will?["topic"]) is { Length: > 0 } willTopic &&
                int.TryParse(Renderer.Scalar(answer?["protocolVersion"]), NumberStyles.None, CultureInfo.InvariantCulture, out var protocolVersion) &&
                int.TryParse(Renderer.Scalar(answer?["subscribeQos"]), NumberStyles.None, CultureInfo.InvariantCulture, out var subscribeQos) &&
                int.TryParse(Renderer.Scalar(will?["qos"]), NumberStyles.None, CultureInfo.InvariantCulture, out var willQos))
            {
                // The API names the broker, and the token is that broker's password: an answer naming another host
                // than the environment's would send the token there, so it is refused before anything connects.
                if (!string.Equals(broker.Host, pinned.Host, StringComparison.OrdinalIgnoreCase) || broker.Port != pinned.Port)
                {
                    throw new
                        CliFailure($"{SubscriberOperation} answered a broker at {broker.Host}:{broker.Port}, and the environment {context.Environment.Name}'s broker is {pinned.Host}:{pinned.Port}; the token is not sent there.");
                }

                return new BrokerSession(broker,
                                         subscriberId,
                                         protocolVersion,
                                         subscribeQos,
                                         willTopic,
                                         willQos);
            }

            throw new
                CliFailure($"{SubscriberOperation} answered no subscriber to connect as: a broker address, a subscriber id, a protocol version, a quality of service and a last will are all needed.");
        }

        private static async Task<ApiResponse> SendAsync(Operation operation,
                                                         IReadOnlyDictionary<string, string> pathValues,
                                                         JsonNode? body,
                                                         ResolvedContext context,
                                                         GlobalValues globals,
                                                         string token,
                                                         ApiClient client,
                                                         CancellationToken cancellationToken)
        {
            var headers = new List<KeyValuePair<string, string>>();
            WriteSafety.AddIdempotencyKey(operation, globals, headers);
            using var request = RequestBuilder.Build(context.Environment,
                                                     operation.Method,
                                                     operation.Template,
                                                     pathValues,
                                                     [],
                                                     body != null ? BodyInput.JsonContent(body) : null,
                                                     headers);
            var response = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            return response.IsSuccess ? response : throw new ApiAnswerException(response, operation);
        }

        /// <summary>
        ///     One message as the command prints it: the payload alone, on one line in JSON mode so that a stream of them is
        ///     a line-delimited one, and as any object's table otherwise.
        /// </summary>
        private static void Write(BusMessage message, GlobalValues globals, CliServices services)
        {
            JsonNode? payload;
            try
            {
                payload = message.Payload.IsEmpty ? null : JsonNode.Parse(message.Payload.Span);
            }
            catch (JsonException)
            {
                payload = JsonValue.Create(Encoding.UTF8.GetString(message.Payload.Span));
            }

            var body = Fields.Project(payload, globals.Fields);
            if (globals.Json)
            {
                services.Out.WriteLine(body == null ? "null" : body.ToJsonString());
            }
            else
            {
                Renderer.WriteTable(body, globals, services);
            }
        }

        private static Operation OperationOf(IReadOnlyList<Operation> operations, string id)
        {
            return operations.FirstOrDefault(operation => operation.Id == id) ??
                   throw new CliFailure($"{id} is not in the committed document; the watch cannot be set up without it.");
        }

        private static string HandOutChoices(Channel channel)
        {
            var names = channel.Parameters.Where(parameter => parameter.OptionName != null).ToDictionary(parameter => parameter.Name, parameter => "--" + parameter.OptionName);
            return string.Join(", or ",
                               channel.HandOuts.Select(handOut => handOut.Selects.Count == 0 ? "no option" : string.Join(" and ", handOut.Selects.Select(name => names[name]))));
        }

        /// <summary>Two ids are one when both are UUIDs of one value, whatever their case; anything else compares as written.</summary>
        private static bool Same(string member, string value)
        {
            return Guid.TryParse(member, out var left) && Guid.TryParse(value, out var right) ? left == right : string.Equals(member, value, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A token for each connect. A token piped in is one line of standard input that cannot be read twice, so it is
        ///     kept; every other source is asked again and refreshes what has expired.
        /// </summary>
        private sealed class TokenCache
        {
            private readonly ResolvedContext _context;

            private readonly GlobalValues _globals;

            private readonly CliServices _services;

            private string? _piped;

            public TokenCache(GlobalValues globals, ResolvedContext context, CliServices services)
            {
                _globals = globals;
                _context = context;
                _services = services;
            }

            public async Task<string> NextAsync(CancellationToken cancellationToken)
            {
                if (_piped != null)
                {
                    return _piped;
                }

                var token = await TokenSource.ResolveAsync(_globals, _context, _services, cancellationToken);
                if (TokenSource.Plan(_globals, _context, _services).Origin == TokenOrigin.StandardInput)
                {
                    _piped = token;
                }

                return token;
            }
        }
    }

    /// <summary>The API refused a call a watch makes on the way; the answer is rendered as any command's.</summary>
    internal sealed class ApiAnswerException : Exception
    {
        public ApiResponse Response { get; }

        public Operation Operation { get; }

        public ApiAnswerException(ApiResponse response, Operation operation) : base($"{operation.Id} answered {response.Status}.")
        {
            Response = response;
            Operation = operation;
        }
    }
}