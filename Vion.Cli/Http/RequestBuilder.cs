using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Vion.Cli.Context;

namespace Vion.Cli.Http
{
    /// <summary>
    ///     The request for an operation: the template with its path parameters substituted, the query appended, the body
    ///     and headers set.
    /// </summary>
    internal static class RequestBuilder
    {
        public static HttpRequestMessage Build(ApiEnvironment environment,
                                               string method,
                                               string template,
                                               IReadOnlyDictionary<string, string> pathValues,
                                               IReadOnlyList<KeyValuePair<string, string>> query,
                                               HttpContent? content,
                                               IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            var path = Expand(template, pathValues);
            var url = new StringBuilder(environment.ApiUrl.TrimEnd('/'));
            url.Append(path.StartsWith('/') ? path : "/" + path);
            if (query.Count > 0)
            {
                url.Append(path.Contains('?', StringComparison.Ordinal) ? '&' : '?');
                url.Append(string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
            }

            var request = new HttpRequestMessage(new HttpMethod(method), url.ToString()) { Content = content };
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return request;
        }

        /// <summary>
        ///     Each `{name}` in the template becomes its value, escaped; a name with no value is left as is, which the caller
        ///     has already refused.
        /// </summary>
        public static string Expand(string template, IReadOnlyDictionary<string, string> values)
        {
            var sb = new StringBuilder(template.Length + 32);
            var i = 0;
            while (i < template.Length)
            {
                var open = template.IndexOf('{', i);
                if (open < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                var close = template.IndexOf('}', open);
                if (close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                sb.Append(template, i, open - i);
                var name = template[(open + 1)..close];
                sb.Append(values.TryGetValue(name, out var value) ? Uri.EscapeDataString(value) : template[open..(close + 1)]);
                i = close + 1;
            }

            return sb.ToString();
        }
    }
}