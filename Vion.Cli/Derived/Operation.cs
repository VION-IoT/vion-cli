using System.Collections.Generic;

namespace Vion.Cli.Derived
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

    internal enum Effect
    {
        Read,

        Reversible,

        Irreversible,
    }

    /// <summary>
    ///     One path or query parameter as the tool takes it. <see cref="OptionName" /> is null when a global supplies the
    ///     value, and <see cref="Global" /> then names which: a context key (<c>tenant</c>, <c>integrator</c>,
    ///     <c>platform</c>, <c>project</c>) or <c>dry-run</c>.
    /// </summary>
    internal sealed record Parameter(
        string Name,
        string? OptionName,
        string? Global,
        ParameterLocation In,
        bool Required,
        ParameterKind Kind,
        string Description);

    /// <summary>A request body: JSON with its schema's name, or multipart with the file part and the text parts.</summary>
    internal sealed record Body(string ContentType, string? SchemaName, string? FilePart, IReadOnlyList<string> TextParts)
    {
        public bool IsMultipart
        {
            get => ContentType == "multipart/form-data";
        }
    }

    internal sealed record Authorization(string Mode, IReadOnlyList<string> Permissions, string? ScopeType, string? ScopeFrom);

    /// <summary>One operation of the document, as <c>Operations.g.cs</c> carries it. The tool reads; it never derives.</summary>
    internal sealed record Operation(
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
        IReadOnlyList<Parameter> Parameters,
        Body? Body,
        Effect Effect,
        bool Idempotent,
        bool Versioned,
        bool DryRun,
        bool Deprecated,
        Authorization Authorization,
        bool Hidden,
        string? Alias)
    {
        /// <summary>
        ///     The <c>system</c> domain stays out of the tree: device and bridge protocols, and the catalog read the tool
        ///     calls itself.
        /// </summary>
        public bool InTree
        {
            get => Domain != "system";
        }

        public string CommandPath
        {
            get => $"{CommandDomain} {CommandAggregate} {CommandVerb}";
        }
    }
}