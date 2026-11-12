namespace Vion.Cli.Derive
{
    /// <summary>The derive step as one call, so a test runs exactly what the command line runs.</summary>
    internal static class Deriver
    {
        public static DerivedDocument Derive(string documentJson, string presentationJson)
        {
            return DocumentReader.Read(documentJson, Presentation.Parse(presentationJson));
        }

        /// <summary>The channels of the event document, checked against the operations the OpenAPI document derived to.</summary>
        public static DerivedChannels DeriveChannels(string eventDocumentJson, DerivedDocument operations)
        {
            return EventDocumentReader.Read(eventDocumentJson, operations);
        }
    }
}