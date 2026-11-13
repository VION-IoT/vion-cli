using System.Collections.Generic;

namespace Vion.Cli.Derived
{
    /// <summary>
    ///     One address parameter of a channel the caller supplies. <see cref="OptionName" /> is null when the context
    ///     supplies the value, and <see cref="Global" /> then names the context key.
    /// </summary>
    internal sealed record ChannelParameter(string Name, string? OptionName, string? Global, bool Required, string Description);

    /// <summary>
    ///     What an address parameter equals on the way to the row: a parameter of the reading operation, or a member of
    ///     the row, as a path below it.
    /// </summary>
    internal sealed record Join(string ChannelParameter, string? OperationParameter, string? Member);

    /// <summary>
    ///     One row that hands a channel out. <see cref="Row" /> is the row's place in the answer of
    ///     <see cref="Operation" />, <c>[]</c> marking a list; <see cref="Selects" /> are the address parameters a caller
    ///     gives as options to mean this hand-out.
    /// </summary>
    internal sealed record HandOut(string Key, string Operation, string Row, IReadOnlyList<Join> Joins, IReadOnlyList<string> Selects);

    /// <summary>One value a registration takes: from an address parameter, or from the registration's own input.</summary>
    internal sealed record Fill(string? ChannelParameter, bool FromInput, string? OperationParameter, string? BodyMember);

    /// <summary>
    ///     The operation a channel is registered through. <see cref="InputOptionName" /> is the repeatable option for the
    ///     one input the address does not carry; the row supplies its values at <see cref="InputRowMember" />.
    /// </summary>
    internal sealed record Registration(string Operation, string SubscriberIdMember, IReadOnlyList<Fill> Fills, string? InputOptionName, string? InputRowMember);

    /// <summary>
    ///     One operation of the event document, as <c>Channels.g.cs</c> carries it: <see cref="Contract" /> is the JSON
    ///     object of its channel's `parameters`, `x-vion-handedOutBy` and `x-vion-registeredBy`, as the document has them.
    /// </summary>
    internal sealed record EventOperation(string Id, string Action, string Channel, string Contract);

    /// <summary>
    ///     One channel of the event document a row hands out, as <c>Channels.g.cs</c> carries it.
    ///     <see cref="OperationId" /> is the event document's operation that sends on it, and <see cref="Protocol" /> the
    ///     protocol the document's servers declare.
    /// </summary>
    internal sealed record Channel(
        string Id,
        string OperationId,
        string Protocol,
        string CommandDomain,
        string CommandAggregate,
        string Description,
        string MessageName,
        IReadOnlyList<ChannelParameter> Parameters,
        IReadOnlyList<HandOut> HandOuts,
        Registration? Registration)
    {
        public const string Verb = "watch";

        public string CommandPath
        {
            get => $"{CommandDomain} {CommandAggregate} {Verb}";
        }
    }
}