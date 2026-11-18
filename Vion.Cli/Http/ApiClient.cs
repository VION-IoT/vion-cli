using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Http
{
    /// <summary>
    ///     What came back: the status, the body as JSON where it was JSON, the text otherwise, and the headers the tool
    ///     reads.
    /// </summary>
    internal sealed record ApiResponse(
        int Status,
        JsonNode? Json,
        string? Text,
        bool HasBody,
        bool Replayed,
        string? WwwAuthenticate,
        string? ETag,
        string? NextCursorHint)
    {
        /// <summary>The status line's reason phrase, as the host sent it.</summary>
        public string? Reason { get; init; }

        /// <summary>Every header of the answer, content headers included, in the order received; a repeated one joined by commas.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

        /// <summary>Where a long-running operation is read, as the host wrote it: a path, normally.</summary>
        public string? OperationLocation { get; init; }

        /// <summary>How long the host asks the caller to wait before reading again.</summary>
        public TimeSpan? RetryAfter { get; init; }

        /// <summary>The `Deprecation` header as sent: present only on an answer of a deprecated operation.</summary>
        public string? Deprecation { get; init; }

        /// <summary>The `Sunset` header as sent, an HTTP date.</summary>
        public string? Sunset { get; init; }

        /// <summary>The target of the `Link` whose relation is `deprecation`, as the host wrote it: a path, normally.</summary>
        public string? DeprecationLink { get; init; }

        public bool IsSuccess
        {
            get => Status is >= 200 and < 300;
        }
    }

    /// <summary>
    ///     One `HttpClient` over the injected handler; sends with the bearer token and reads the answer into an
    ///     <see cref="ApiResponse" />.
    /// </summary>
    internal sealed class ApiClient : IDisposable
    {
        private readonly HttpClient _client;

        private readonly CliServices _services;

        public ApiClient(CliServices services)
        {
            _services = services;
            _client = new HttpClient(services.Handler, false) { Timeout = TimeSpan.FromSeconds(100) };
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("vion", services.Version));
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public async Task<ApiResponse> SendAsync(HttpRequestMessage request, string token, bool verbose, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (verbose)
            {
                _services.Error.WriteLine($"> {request.Method} {request.RequestUri}");
                foreach (var header in request.Headers.Where(h => h.Key != "Authorization"))
                {
                    _services.Error.WriteLine($"> {header.Key}: {string.Join(", ", header.Value)}");
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new NoAnswer($"{request.Method} {request.RequestUri}: {exception.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new NoAnswer($"{request.Method} {request.RequestUri}: no answer within {_client.Timeout.TotalSeconds:0} seconds.");
            }

            using (response)
            {
                if (verbose)
                {
                    _services.Error.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");
                    foreach (var header in response.Headers)
                    {
                        _services.Error.WriteLine($"< {header.Key}: {string.Join(", ", header.Value)}");
                    }
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                var hasBody = text.Length > 0;
                JsonNode? json = null;
                var isJson = response.Content.Headers.ContentType?.MediaType is { } mediaType && mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase);
                if (hasBody && isJson)
                {
                    try
                    {
                        json = JsonNode.Parse(text);
                    }
                    catch (JsonException)
                    {
                        json = null;
                    }
                }

                var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var replayedValues) &&
                               replayedValues.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
                var wwwAuthenticate = response.Headers.WwwAuthenticate.Count > 0 ? string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString())) : null;
                var etag = response.Headers.ETag?.Tag;
                var headers = response.Headers
                                      .Concat(response.Content.Headers)
                                      .Select(header => new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)))
                                      .ToList();
                var location = response.Headers.TryGetValues("Operation-Location", out var locations) ? locations.FirstOrDefault() : null;
                var retryAfter = response.Headers.RetryAfter is { } retry ? retry.Delta ?? retry.Date - _services.Time.GetUtcNow() : null;
                return new ApiResponse((int)response.StatusCode,
                                       json,
                                       json == null && hasBody ? text : null,
                                       hasBody,
                                       replayed,
                                       wwwAuthenticate,
                                       etag,
                                       null)
                       {
                           Reason = response.ReasonPhrase,
                           Headers = headers,
                           OperationLocation = location,
                           RetryAfter = retryAfter,
                           Deprecation = response.Headers.TryGetValues("Deprecation", out var deprecations) ? deprecations.FirstOrDefault() : null,
                           Sunset = response.Headers.TryGetValues("Sunset", out var sunsets) ? sunsets.FirstOrDefault() : null,
                           DeprecationLink = response.Headers.TryGetValues("Link", out var links) ? DeprecationTarget(links) : null,
                       };
            }
        }

        /// <summary>
        ///     The target of the first link whose `rel` names `deprecation`. A `Link` header carries comma-separated links,
        ///     each `&lt;target&gt;` followed by `;`-separated parameters, and a `rel` may name several space-separated
        ///     relations.
        /// </summary>
        private static string? DeprecationTarget(IEnumerable<string> values)
        {
            foreach (var link in values.SelectMany(value => value.Split(',')))
            {
                var parts = link.Split(';', StringSplitOptions.TrimEntries);
                if (parts[0] is not ['<', .., '>'])
                {
                    continue;
                }

                var relations = parts.Skip(1)
                                     .Where(part => part.StartsWith("rel=", StringComparison.OrdinalIgnoreCase))
                                     .SelectMany(part => part["rel=".Length..].Trim('"').Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (relations.Contains("deprecation", StringComparer.OrdinalIgnoreCase))
                {
                    return parts[0][1..^1];
                }
            }

            return null;
        }
    }
}