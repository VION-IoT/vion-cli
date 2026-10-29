namespace Vion.Cli.Derive
{
    /// <summary>The derive step as one call, so a test runs exactly what the command line runs.</summary>
    internal static class Deriver
    {
        public static DerivedDocument Derive(string documentJson, string presentationJson)
        {
            return DocumentReader.Read(documentJson, Presentation.Parse(presentationJson));
        }
    }
}