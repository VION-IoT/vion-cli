using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Auth
{
    /// <summary>
    ///     What the realm handed out for a device code: where the person goes, what they type, how long and how often to
    ///     ask.
    /// </summary>
    internal sealed record DeviceCode(string Code, string UserCode, Uri VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval);

    /// <summary>
    ///     The realm's two OpenID Connect endpoints as the tool uses them: the device authorization, and the token endpoint
    ///     for the device grant, the client-credentials grant and a refresh. Form-encoded in, a JSON object out.
    /// </summary>
    internal sealed class OAuthClient : IDisposable
    {
        private const string Scopes = "openid user_impersonation web_mqtt_access";

        private readonly HttpClient _client;

        private readonly CliServices _services;

        public OAuthClient(CliServices services)
        {
            _services = services;
            _client = new HttpClient(services.Handler, false) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public async Task<DeviceCode> RequestDeviceCodeAsync(ApiEnvironment environment, CancellationToken cancellationToken)
        {
            var answer = await PostAsync($"{Realm(environment)}/auth/device",
                                         [new KeyValuePair<string, string>("client_id", environment.ClientId), new KeyValuePair<string, string>("scope", Scopes)],
                                         cancellationToken);
            if (!answer.IsSuccess)
            {
                throw new CliFailure($"{environment.Name}: the realm refused the device code request: {Describe(answer.Body)}");
            }

            var body = answer.Body;
            var complete = Text(body, "verification_uri_complete") ??
                           Text(body, "verification_uri") ?? throw new CliFailure("the realm's device answer names no verification URL.");
            return new DeviceCode(Text(body, "device_code") ?? throw new CliFailure("the realm's device answer carries no device_code."),
                                  Text(body, "user_code") ?? "",
                                  new Uri(complete),
                                  TimeSpan.FromSeconds(Number(body, "expires_in") ?? 600),
                                  TimeSpan.FromSeconds(Number(body, "interval") ?? 5));
        }

        /// <summary>
        ///     Polls the token endpoint at the granted interval, five seconds longer after each `slow_down`, until a token,
        ///     a refusal or the code's expiry; every wait is on the tool's clock.
        /// </summary>
        public async Task<StoredCredentials> PollDeviceTokenAsync(ApiEnvironment environment, DeviceCode device, CancellationToken cancellationToken)
        {
            var deadline = _services.Time.GetUtcNow() + device.ExpiresIn;
            var interval = device.Interval;
            while (true)
            {
                await Task.Delay(interval, _services.Time, cancellationToken);
                if (_services.Time.GetUtcNow() >= deadline)
                {
                    throw new CliFailure("the device code expired before the sign-in completed; run `vion auth login` again.");
                }

                var answer = await PostAsync($"{Realm(environment)}/token",
                                             [
                                                 new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                                                 new KeyValuePair<string, string>("client_id", environment.ClientId),
                                                 new KeyValuePair<string, string>("device_code", device.Code),
                                             ],
                                             cancellationToken);
                if (answer.IsSuccess)
                {
                    return Credentials(answer.Body, "device");
                }

                switch (Text(answer.Body, "error"))
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += TimeSpan.FromSeconds(5);
                        continue;
                    default:
                        throw new CliFailure($"the sign-in did not complete: {Describe(answer.Body)}");
                }
            }
        }

        public async Task<StoredCredentials> ClientCredentialsAsync(ApiEnvironment environment, string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            var answer = await PostAsync($"{Realm(environment)}/token",
                                         [
                                             new KeyValuePair<string, string>("grant_type", "client_credentials"), new KeyValuePair<string, string>("client_id", clientId),
                                             new KeyValuePair<string, string>("client_secret", clientSecret), new KeyValuePair<string, string>("scope", "openid"),
                                         ],
                                         cancellationToken);
            if (!answer.IsSuccess)
            {
                throw new CliFailure($"{environment.Name}: the realm refused the client credentials of {clientId}: {Describe(answer.Body)}");
            }

            return Credentials(answer.Body, "client-credentials");
        }

        public async Task<StoredCredentials?> RefreshAsync(ApiEnvironment environment, string refreshToken, CancellationToken cancellationToken)
        {
            var answer = await PostAsync($"{Realm(environment)}/token",
                                         [
                                             new KeyValuePair<string, string>("grant_type", "refresh_token"), new KeyValuePair<string, string>("client_id", environment.ClientId),
                                             new KeyValuePair<string, string>("refresh_token", refreshToken),
                                         ],
                                         cancellationToken);
            return answer.IsSuccess ? Credentials(answer.Body, "device") : null;
        }

        private static string Realm(ApiEnvironment environment)
        {
            return environment.AuthUrl.TrimEnd('/') + "/protocol/openid-connect";
        }

        private StoredCredentials Credentials(JsonObject? body, string obtainedBy)
        {
            var accessToken = Text(body, "access_token") ?? throw new CliFailure("the realm's token answer carries no access_token.");
            var expiresIn = Number(body, "expires_in") ?? 300;
            return new StoredCredentials(accessToken, Text(body, "refresh_token"), _services.Time.GetUtcNow().AddSeconds(expiresIn), obtainedBy);
        }

        private async Task<(bool IsSuccess, JsonObject? Body)> PostAsync(string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(form);
            HttpResponseMessage response;
            try
            {
                response = await _client.PostAsync(new Uri(url), content, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new CliFailure($"POST {url}: {exception.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CliFailure($"POST {url}: no answer within {_client.Timeout.TotalSeconds:0} seconds.");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonObject? body = null;
                if (text.Length > 0)
                {
                    try
                    {
                        body = JsonNode.Parse(text) as JsonObject;
                    }
                    catch (JsonException)
                    {
                        body = new JsonObject { ["error"] = $"HTTP {(int)response.StatusCode}", ["error_description"] = text };
                    }
                }

                return (response.IsSuccessStatusCode, body);
            }
        }

        private static string Describe(JsonObject? body)
        {
            var error = Text(body, "error") ?? "no error named";
            var description = Text(body, "error_description");
            return description != null ? $"{error} ({description})" : error;
        }

        private static string? Text(JsonObject? node, string member)
        {
            return node?[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }

        private static double? Number(JsonObject? node, string member)
        {
            return node?[member] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;
        }
    }
}