using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>One request as the tool sent it: what a criterion about the wire asserts on.</summary>
    internal sealed record RecordedRequest(string Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body, string? ContentType);

    /// <summary>Stands in for the API: answers what the test arranged, records every request, never touches the network.</summary>
    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public List<RecordedRequest> Requests { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        /// <summary>A handler answering every request with one JSON body, the headers configured on it.</summary>
        public static RecordingHandler Json(int status, string json, Action<HttpResponseMessage>? configure = null)
        {
            return new RecordingHandler(_ =>
                                        {
                                            var response = Response(status, json);
                                            configure?.Invoke(response);
                                            return response;
                                        });
        }

        /// <summary>A handler answering with a status and no body at all, as the host does for a route it lacks.</summary>
        public static RecordingHandler Empty(int status)
        {
            return new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
        }

        /// <summary>One JSON answer, for a handler that routes by request.</summary>
        public static HttpResponseMessage Response(int status, string json)
        {
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, new UTF8Encoding(false), "application/json") };
        }

        /// <summary>The requests sent to a URL containing the text, in order.</summary>
        public IReadOnlyList<RecordedRequest> To(string urlPart)
        {
            return Requests.Where(request => request.Uri.ToString().Contains(urlPart, StringComparison.Ordinal)).ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            string? body = null;
            string? contentType = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                contentType = request.Content.Headers.ContentType?.ToString();
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!, headers, body, contentType));
            return _respond(request);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}