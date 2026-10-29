using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Cli.Derived;
using Vion.Cli.Http;

namespace Vion.Cli.Rendering
{
    /// <summary>
    ///     An error answer as the user reads it: the status and message, and what the API's `ApiExceptionDto` adds — the
    ///     `problems`, the `requiredPermissions` of a 403, the `currentVersion` of a 412, the `exceptionId` that finds the
    ///     server log. A 401 has no body by design and carries its reason in `WWW-Authenticate`; a 404 with no body is the
    ///     host's own, for a route it does not have.
    /// </summary>
    internal sealed record ApiError(
        int Status,
        string? StatusCode,
        string Message,
        IReadOnlyList<(string? Code, string? Message)> Problems,
        IReadOnlyList<string> RequiredPermissions,
        string? CurrentVersion,
        string? ExceptionId,
        string? Hint)
    {
        public static ApiError From(ApiResponse response, Operation? operation)
        {
            var body = response.Json as JsonObject;
            var message = Text(body, "message");
            string? hint = null;
            switch (response.Status)
            {
                case 401 when body == null:
                    message = "unauthenticated" + (response.WwwAuthenticate != null ? $": {response.WwwAuthenticate}" : "");
                    hint = "sign in with `vion auth login`, or pass a fresh token.";
                    break;
                case 404 when body == null && operation != null:
                    message = $"{operation.Id} is not in this environment's document: {operation.Method} {operation.Template} answered 404 with no body.";
                    hint = "`vion ops --live` lists what the environment serves against the committed document.";
                    break;
                default:
                    message ??= response.Text ?? "";
                    break;
            }

            var problems = new List<(string?, string?)>();
            if (body?["problems"] is JsonArray problemArray)
            {
                problems.AddRange(problemArray.OfType<JsonObject>().Select(problem => (Text(problem, "code"), Text(problem, "message"))));
            }

            var permissions = body?["requiredPermissions"] is JsonArray permissionArray ? permissionArray.Select(p => p?.ToString() ?? "").ToList() : [];
            return new ApiError(response.Status,
                                Text(body, "statusCode"),
                                message,
                                problems,
                                permissions,
                                Text(body, "currentVersion"),
                                Text(body, "exceptionId"),
                                hint);
        }

        public IEnumerable<string> Lines()
        {
            yield return $"error {Status}{(StatusCode != null ? " " + StatusCode : "")}: {Message}";

            foreach (var (code, text) in Problems)
            {
                yield return $"  - {code}: {text}";
            }

            if (RequiredPermissions.Count > 0)
            {
                yield return $"  required permissions: {string.Join(", ", RequiredPermissions)}";
            }

            if (CurrentVersion != null)
            {
                yield return $"  current version: {CurrentVersion}";
            }

            if (ExceptionId != null)
            {
                yield return $"  exception id: {ExceptionId}";
            }

            if (Hint != null)
            {
                yield return $"  {Hint}";
            }
        }

        public JsonObject ToJson()
        {
            var json = new JsonObject
                       {
                           ["status"] = Status,
                           ["statusCode"] = StatusCode,
                           ["message"] = Message,
                       };
            if (Problems.Count > 0)
            {
                json["problems"] = new JsonArray(Problems.Select(p => (JsonNode)new JsonObject { ["code"] = p.Code, ["message"] = p.Message }).ToArray());
            }

            if (RequiredPermissions.Count > 0)
            {
                json["requiredPermissions"] = new JsonArray(RequiredPermissions.Select(p => (JsonNode)JsonValue.Create(p)).ToArray());
            }

            if (CurrentVersion != null)
            {
                json["currentVersion"] = CurrentVersion;
            }

            if (ExceptionId != null)
            {
                json["exceptionId"] = ExceptionId;
            }

            if (Hint != null)
            {
                json["hint"] = Hint;
            }

            return json;
        }

        private static string? Text(JsonObject? node, string member)
        {
            return node?[member] switch
            {
                null => null,
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                var other => other.ToJsonString(),
            };
        }
    }
}