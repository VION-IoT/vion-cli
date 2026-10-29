using System.Collections.Generic;

namespace Vion.Cli.Derive
{
    internal enum ParameterLocation
    {
        Path,

        Query,
    }

    internal enum ParameterKind
    {
        String,

        Uuid,

        Boolean,

        Integer,

        DateTime,

        StringArray,
    }

    /// <summary>
    ///     One path or query parameter as the tool will take it. <see cref="OptionName" /> is null when a global supplies
    ///     the value instead, and <see cref="Global" /> then names which: a context key for the root and project
    ///     parameters, <c>dry-run</c> for the dry-run query flag.
    /// </summary>
    internal sealed record DerivedParameter(
        string Name,
        string? OptionName,
        string? Global,
        ParameterLocation In,
        bool Required,
        ParameterKind Kind,
        string Description);

    internal sealed record DerivedBody(string ContentType, string? SchemaName, string? FilePart, IReadOnlyList<string> TextParts);

    internal sealed record DerivedAuthorization(string Mode, IReadOnlyList<string> Permissions, string? ScopeType, string? ScopeFrom);

    internal sealed record DerivedOperation(
        string Id,
        string Domain,
        string Aggregate,
        string Verb,
        string CommandDomain,
        string CommandAggregate,
        string CommandVerb,
        string Method,
        string Template,
        string Description,
        IReadOnlyList<DerivedParameter> Parameters,
        DerivedBody? Body,
        string Effect,
        bool Idempotent,
        bool Versioned,
        bool DryRun,
        bool Deprecated,
        DerivedAuthorization Authorization,
        bool Hidden,
        string? Alias);

    internal sealed record DerivedDocument(string SnapshotSha256, string ApiVersion, IReadOnlyList<DerivedOperation> Operations);

    /// <summary>A refusal of the input: a malformed document, or a cli.json entry the document lacks.</summary>
    internal sealed class DeriveException : System.Exception
    {
        public DeriveException(string message) : base(message)
        {
        }
    }
}