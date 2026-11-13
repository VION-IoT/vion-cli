using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;

namespace Vion.Cli.Commands
{
    /// <summary>The values of the global options for one invocation, read once off the parse result.</summary>
    internal sealed record GlobalValues(
        string? Env,
        string? Integrator,
        string? Tenant,
        string? Project,
        string? Platform,
        bool Json,
        IReadOnlyList<string> Fields,
        bool Yes,
        bool DryRun,
        string? IdempotencyKey,
        string? IfMatch,
        string? Token,
        string? ClientId,
        string? ClientSecret,
        bool Verbose,
        bool Wait,
        TimeSpan? Timeout)
    {
        /// <summary>How long `--wait` waits when `--timeout` is absent: past the host's run timeout, queue time on top.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);
    }

    /// <summary>The body of a call as the user gave it: a document, raw pairs, typed pairs, a file.</summary>
    internal sealed record BodyValues(string? Input, IReadOnlyList<string> RawFields, IReadOnlyList<string> Fields, string? File)
    {
        public bool Any
        {
            get => Input != null || RawFields.Count > 0 || Fields.Count > 0 || File != null;
        }
    }

    /// <summary>
    ///     The options every command takes, recursive from the root. Their names are the list the derive step suffixes a
    ///     colliding parameter against; a test holds the two lists equal.
    /// </summary>
    internal sealed class GlobalOptions
    {
        /// <summary>The longest `--timeout` taken: a week, far past any run the host lets live, and far from overflow.</summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(7);

        public Option<string> Env { get; } =
            new("--env") { Description = "The environment: production, test, or one defined with `context use`.", Recursive = true, HelpName = "name" };

        public Option<string> Integrator { get; } = new("--integrator")
                                                    { Description = "The integrator id, filling {integratorId} for this call.", Recursive = true, HelpName = "id" };

        public Option<string> Tenant { get; } = new("--tenant") { Description = "The tenant id, filling {tenantId} for this call.", Recursive = true, HelpName = "id" };

        public Option<string> Project { get; } = new("--project") { Description = "The project id, filling projectId for this call.", Recursive = true, HelpName = "id" };

        public Option<string> Platform { get; } = new("--platform") { Description = "The platform id, filling {platformId} for this call.", Recursive = true, HelpName = "id" };

        public Option<bool> Json { get; } = new("--json") { Description = "Write the response as JSON, and nothing else, to standard output.", Recursive = true };

        public Option<string> Fields { get; } =
            new("--fields") { Description = "Keep only these members, comma-separated, dotted for a nested one.", Recursive = true, HelpName = "a,b.c" };

        public Option<bool> Yes { get; } = new("--yes") { Description = "Confirm an irreversible operation without asking.", Recursive = true };

        public Option<bool> DryRun { get; } = new("--dry-run") { Description = "Check the request without applying it, on an operation that offers it.", Recursive = true };

        public Option<string> IdempotencyKey { get; } = new("--idempotency-key")
                                                        { Description = "The Idempotency-Key to send on a write; a fresh one when absent.", Recursive = true, HelpName = "key" };

        public Option<string> IfMatch { get; } =
            new("--if-match") { Description = "The version to send as If-Match on a versioned update.", Recursive = true, HelpName = "version" };

        public Option<string> Token { get; } = new("--token") { Description = "Read the bearer token from standard input: `--token -`.", Recursive = true, HelpName = "-" };

        public Option<string> ClientId { get; } = new("--client-id") { Description = "A service account's client id, with --client-secret.", Recursive = true, HelpName = "id" };

        public Option<string> ClientSecret { get; } = new("--client-secret")
                                                      { Description = "A service account's client secret, with --client-id.", Recursive = true, HelpName = "secret" };

        public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show the request and the response headers on standard error.", Recursive = true };

        public Option<bool> Wait { get; } = new("--wait")
                                            {
                                                Description =
                                                    "Wait for a long-running operation to end and exit by its outcome; a command whose answer is already the outcome ignores it.",
                                                Recursive = true,
                                            };

        /// <summary>`--timeout`'s help on a watch, where it bounds the watch itself rather than a `--wait`.</summary>
        public const string WatchTimeoutDescription =
            "How long to watch once subscribed: 90s, 15m, 1h or seconds; until interrupted when absent. Exits 0 then, 1 if never subscribed.";

        public Option<string> Timeout { get; } = CreateTimeout();

        public IEnumerable<Option> All
        {
            get
            {
                yield return Env;
                yield return Integrator;
                yield return Tenant;
                yield return Project;
                yield return Platform;
                yield return Json;
                yield return Fields;
                yield return Yes;
                yield return DryRun;
                yield return IdempotencyKey;
                yield return IfMatch;
                yield return Token;
                yield return ClientId;
                yield return ClientSecret;
                yield return Verbose;
                yield return Wait;
                yield return Timeout;
            }
        }

        public GlobalValues Read(ParseResult result)
        {
            var fields = result.GetValue(Fields);
            return new GlobalValues(result.GetValue(Env),
                                    result.GetValue(Integrator),
                                    result.GetValue(Tenant),
                                    result.GetValue(Project),
                                    result.GetValue(Platform),
                                    result.GetValue(Json),
                                    string.IsNullOrWhiteSpace(fields) ? [] : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                                    result.GetValue(Yes),
                                    result.GetValue(DryRun),
                                    result.GetValue(IdempotencyKey),
                                    result.GetValue(IfMatch),
                                    result.GetValue(Token),
                                    result.GetValue(ClientId),
                                    result.GetValue(ClientSecret),
                                    result.GetValue(Verbose),
                                    result.GetValue(Wait),
                                    TryParseDuration(result.GetValue(Timeout), out var timeout) ? timeout : null);
        }

        /// <summary>
        ///     A duration as `90s`, `15m`, `1h`, or bare seconds, up to <see cref="MaxTimeout" />; anything else, or zero, is
        ///     not one.
        /// </summary>
        public static bool TryParseDuration(string? text, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var unit = char.IsAsciiLetter(text[^1]) ? char.ToLowerInvariant(text[^1]) : 's';
            var digits = char.IsAsciiLetter(text[^1]) ? text[..^1] : text;
            long seconds = unit switch
            {
                's' => 1,
                'm' => 60,
                'h' => 3600,
                _ => 0,
            };
            if (seconds == 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > MaxTimeout.TotalSeconds / seconds)
            {
                return false;
            }

            duration = TimeSpan.FromSeconds(amount * seconds);
            return true;
        }

        private static Option<string> CreateTimeout()
        {
            var option = new Option<string>("--timeout")
                         {
                             Description = "How long --wait waits: 90s, 15m, 1h or seconds; 15m when absent. Still running then exits 3.",
                             Recursive = true,
                             HelpName = "duration",
                         };
            option.Validators.Add(result =>
                                  {
                                      if (result.GetValueOrDefault<string>() is { } value && !TryParseDuration(value, out _))
                                      {
                                          result.AddError($"--timeout takes a duration such as 90s, 15m or 1h, at most 7 days; '{value}' is not one.");
                                      }
                                  });
            return option;
        }
    }

    /// <summary>
    ///     The body options, added to each command that takes a body and to `api`; `--file` only where the body is
    ///     multipart.
    /// </summary>
    internal sealed class BodyOptions
    {
        public Option<string> Input { get; } = new("--input") { Description = "The JSON body from a file, or `-` for standard input.", HelpName = "file|-" };

        public Option<string[]> RawField { get; } = new("--raw-field", "-f") { Description = "A body member as a string: key=value; a dotted key nests.", HelpName = "key=value" };

        public Option<string[]> Field { get; } = new("--field", "-F")
                                                 {
                                                     Description =
                                                         "A body member typed as JSON: true, false, null, a number, @file for a file's content, else a string.",
                                                     HelpName = "key=value",
                                                 };

        public Option<string> File { get; } = new("--file") { Description = "The file to upload as the multipart body's file part.", HelpName = "path" };

        public IEnumerable<Option> ForJson
        {
            get
            {
                yield return Input;
                yield return RawField;
                yield return Field;
            }
        }

        public IEnumerable<Option> ForMultipart
        {
            get
            {
                yield return File;
                yield return RawField;
            }
        }

        public BodyValues Read(ParseResult result)
        {
            return new BodyValues(result.GetValue(Input), result.GetValue(RawField) ?? [], result.GetValue(Field) ?? [], result.GetValue(File));
        }
    }
}