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
        string Completion,
        bool AnswersLongRunningOperation,
        DerivedAuthorization Authorization,
        bool Hidden,
        string? Alias);

    internal sealed record DerivedDocument(string SnapshotSha256, string ApiVersion, IReadOnlyList<DerivedOperation> Operations);

    /// <summary>
    ///     One address parameter of a channel the caller supplies: by an option, or, where <see cref="Global" /> names a
    ///     context key, by the context. A parameter no hand-out joins to select its row is not in the model at all.
    /// </summary>
    internal sealed record DerivedChannelParameter(string Name, string? OptionName, string? Global, bool Required, string Description);

    /// <summary>
    ///     What an address parameter equals on the way to the row: a parameter of the reading operation, or a member of
    ///     the row, written relative to it.
    /// </summary>
    internal sealed record DerivedJoin(string ChannelParameter, string? OperationParameter, string? Member);

    /// <summary>
    ///     One row that hands a channel out: the operation that answers it, the row's place in that answer, the joins
    ///     that pick it, and the options a caller gives to mean this hand-out.
    /// </summary>
    internal sealed record DerivedHandOut(string Key, string Operation, string Row, IReadOnlyList<DerivedJoin> Joins, IReadOnlyList<string> Selects);

    /// <summary>One value a registration takes: from an address parameter, or from the registration's own input.</summary>
    internal sealed record DerivedFill(string? ChannelParameter, bool FromInput, string? OperationParameter, string? BodyMember);

    /// <summary>
    ///     The operation a channel is registered through. <see cref="InputOptionName" /> is the repeatable option for the
    ///     one input the address does not carry, whose values the row supplies at <see cref="InputRowMember" />.
    /// </summary>
    internal sealed record DerivedRegistration(string Operation, string SubscriberIdMember, IReadOnlyList<DerivedFill> Fills, string? InputOptionName, string? InputRowMember);

    internal sealed record DerivedChannel(
        string Id,
        string OperationId,
        string CommandDomain,
        string CommandAggregate,
        string Description,
        string MessageName,
        IReadOnlyList<DerivedChannelParameter> Parameters,
        IReadOnlyList<DerivedHandOut> HandOuts,
        DerivedRegistration? Registration);

    /// <summary>
    ///     One operation of the event document: its id, its action, the channel it names, and that channel's members
    ///     `ops --live` compares — `parameters`, `messages`, `x-vion-handedOutBy`, `x-vion-registeredBy` — as one minified
    ///     JSON object,
    ///     copied as the document has them so that the tool projects the committed side and the served side alike.
    /// </summary>
    internal sealed record DerivedEventOperation(string Id, string Action, string Channel, string Contract);

    /// <summary>The event document as the tool carries it; <see cref="Protocol" /> is the one its servers declare.</summary>
    internal sealed record DerivedChannels(string SnapshotSha256, string Protocol, IReadOnlyList<DerivedChannel> Channels, IReadOnlyList<DerivedEventOperation> Operations);

    /// <summary>A refusal of the input: a malformed document, or a cli.json entry the document lacks.</summary>
    internal sealed class DeriveException : System.Exception
    {
        public DeriveException(string message) : base(message)
        {
        }
    }
}