using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Commands;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Http
{
    /// <summary>
    ///     `--wait`: follows a long-running operation from the answer that named it to its end, for a derived command and
    ///     for `api` alike. The first answer is the first poll — a start may answer already `Succeeded`, a dry run always
    ///     does — so the state decides, never the status. The outcome comes back as an answer the renderer renders as any:
    ///     `result` as the body on success, `error` as an error answer on failure.
    /// </summary>
    internal static class LongRunningWait
    {
        private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        ///     Whether `--wait` follows an answer. The table says so of a start published `LongRunningOperation` and of
        ///     the long-running operation's own read; the host says so of any answer, whatever a table built from an older
        ///     document thinks — `Operation-Location` names something to follow, and `Retry-After` on a read says to read
        ///     it again. A body's shape alone never decides: another resource's row may carry a `state`.
        /// </summary>
        public static bool Follows(Derived.Operation? operation, string method, ApiResponse answer)
        {
            return operation is { Completion: Derived.Completion.LongRunningOperation } || answer.OperationLocation != null ||
                   (method == "GET" && (operation is { AnswersLongRunningOperation: true } || answer.RetryAfter != null));
        }

        /// <summary>
        ///     Waits on <paramref name="first" />. <paramref name="readUri" /> is the request just sent when it was the
        ///     long-running operation's own read, which carries no `Operation-Location`; null otherwise.
        /// </summary>
        public static async Task<ApiResponse> FollowAsync(ApiResponse first,
                                                          Uri? readUri,
                                                          GlobalValues globals,
                                                          ResolvedContext context,
                                                          string token,
                                                          ApiClient client,
                                                          CliServices services,
                                                          CancellationToken cancellationToken)
        {
            if (!first.IsSuccess || first.Json is not JsonObject)
            {
                return first;
            }

            // Said once, here, whichever way the wait ends: a replayed start ran no new work, and the outcome that
            // follows is the first run's.
            if (first.Replayed)
            {
                services.Error.WriteLine(Renderer.ReplayedNotice);
            }

            var pollUri = first.OperationLocation != null ? Resolve(first.OperationLocation, context) : readUri;
            var location = first.OperationLocation ?? readUri?.PathAndQuery;
            var timeout = globals.Timeout ?? GlobalValues.DefaultTimeout;
            var deadline = services.Time.GetUtcNow() + timeout;
            var current = first;
            while (true)
            {
                var handle = (JsonObject)current.Json!;
                var state = handle["state"]?.ToString();
                switch (state)
                {
                    case "Succeeded":
                        if (handle["id"]?.ToString() is { Length: > 0 } id)
                        {
                            services.Error.WriteLine($"long-running operation {id} succeeded.");
                        }

                        return current with { Status = 200, Json = handle["result"]?.DeepClone(), Text = null, Replayed = false, OperationLocation = null };
                    case "Failed":
                        return Failure(handle);
                    case "Queued" or "Running":
                        break;
                    default:
                        // Not a long-running operation's body: there is nothing to wait for.
                        return current with { Replayed = false };
                }

                if (pollUri == null)
                {
                    throw new CliFailure($"the long-running operation is {state} and its answer names no Operation-Location to follow.");
                }

                var left = deadline - services.Time.GetUtcNow();
                if (left <= TimeSpan.Zero)
                {
                    throw new WaitLeftRunning($"still {state} after {Describe(timeout)}: the work goes on. {PickUp(location!)}", handle.DeepClone(), ExitCodes.StillRunning);
                }

                var delay = current.RetryAfter is { } asked && asked > TimeSpan.Zero ? asked : DefaultDelay;
                try
                {
                    await Task.Delay(delay < left ? delay : left, services.Time, cancellationToken);
                    (current, token) = await PollAsync(pollUri,
                                                       globals,
                                                       context,
                                                       token,
                                                       client,
                                                       services,
                                                       cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new WaitLeftRunning($"stopped waiting; the work goes on. {PickUp(location!)}", handle.DeepClone(), ExitCodes.Failure);
                }

                if (!current.IsSuccess)
                {
                    services.Error.WriteLine($"reading the long-running operation failed; the work may go on. {PickUp(location!)}");
                    return current;
                }

                if (current.Json is not JsonObject)
                {
                    throw new CliFailure($"GET {location} answered {current.Status} without a long-running operation.");
                }
            }
        }

        public static string PickUp(string location)
        {
            return $"Pick it up with `vion api GET {location} --wait`.";
        }

        /// <summary>
        ///     `Operation-Location` is a URI reference resolved against the API's URL, so a host's path base, which the
        ///     header already carries, is not doubled; an absolute one on another origin is refused before the token is
        ///     sent to it.
        /// </summary>
        private static Uri Resolve(string location, ResolvedContext context)
        {
            var api = new Uri(context.Environment.ApiUrl.TrimEnd('/') + "/");
            if (!Uri.TryCreate(api, location, out var resolved))
            {
                throw new CliFailure($"Operation-Location '{location}' is not a URI reference.");
            }

            if (Uri.Compare(resolved, api, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new CliFailure($"Operation-Location '{location}' is not on {context.Environment.ApiUrl}; the token is not sent there.");
            }

            return resolved;
        }

        /// <summary>
        ///     One read of the long-running operation. A 401 is answered once by resolving the token again — a stored
        ///     sign-in refreshes, a service account signs in anew — so a wait outlives the token it started with; a token
        ///     piped into `--token -` cannot be read twice and is kept.
        /// </summary>
        private static async Task<(ApiResponse Answer, string Token)> PollAsync(Uri uri,
                                                                                GlobalValues globals,
                                                                                ResolvedContext context,
                                                                                string token,
                                                                                ApiClient client,
                                                                                CliServices services,
                                                                                CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var answer = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            if (answer.Status != 401 || globals.Token != null)
            {
                return (answer, token);
            }

            var fresh = await Auth.TokenSource.ResolveAsync(globals, context, services, cancellationToken);
            using var retry = new HttpRequestMessage(HttpMethod.Get, uri);
            return (await client.SendAsync(retry, fresh, globals.Verbose, cancellationToken), fresh);
        }

        /// <summary>The operation's `error` as the error answer the synchronous call would have given.</summary>
        private static ApiResponse Failure(JsonObject handle)
        {
            var error = handle["error"]?.DeepClone();
            var status = error is JsonObject body && Enum.TryParse<HttpStatusCode>(body["statusCode"]?.ToString(), out var code) && (int)code >= 400 ? (int)code : 500;
            return new ApiResponse(status,
                                   error,
                                   error == null ? "the long-running operation failed without an error body." : null,
                                   true,
                                   false,
                                   null,
                                   null,
                                   null);
        }

        private static string Describe(TimeSpan timeout)
        {
            return timeout.TotalHours >= 1 && timeout.TotalHours % 1 == 0 ? $"{timeout.TotalHours:0}h" :
                   timeout.TotalMinutes >= 1 && timeout.TotalMinutes % 1 == 0 ? $"{timeout.TotalMinutes:0}m" : $"{timeout.TotalSeconds:0}s";
        }
    }
}