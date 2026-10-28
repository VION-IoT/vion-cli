using System;
using System.IO;
using System.Text;

namespace Vion.Cli.Derive
{
    internal static class Program
    {
        /// <summary>
        ///     <c>Vion.Cli.Derive &lt;snapshot&gt; &lt;cli.json&gt; &lt;out dir&gt;</c>: reads the document and the presentation
        ///     file,
        ///     writes <c>Operations.g.cs</c> into the directory. Exit 1 on a refused input, 2 on a usage error.
        /// </summary>
        private static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("usage: Vion.Cli.Derive <openapi/v1.json> <cli.json> <out dir>");
                return 2;
            }

            try
            {
                var document = Deriver.Derive(File.ReadAllText(args[0]), File.ReadAllText(args[1]));
                var target = Path.Combine(args[2], "Operations.g.cs");
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(target, Emitter.Emit(document), new UTF8Encoding(false));
                Console.WriteLine($"derive: {document.Operations.Count} operations, snapshot sha256 {document.SnapshotSha256} -> {target}");
                return 0;
            }
            catch (DeriveException exception)
            {
                Console.Error.WriteLine($"derive: {exception.Message}");
                return 1;
            }
        }
    }
}