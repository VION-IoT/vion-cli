using System;
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
                throw new CliFailure($"{request.Method} {request.RequestUri}: {exception.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CliFailure($"{request.Method} {request.RequestUri}: no answer within {_client.Timeout.TotalSeconds:0} seconds.");
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
                return new ApiResponse((int)response.StatusCode,
                                       json,
                                       json == null && hasBody ? text : null,
                                       hasBody,
                                       replayed,
                                       wwwAuthenticate,
                                       etag,
                                       null);
            }
        }
    }
}