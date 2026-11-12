using System;
using System.IO;
using System.Text;

namespace Vion.Cli.Derive
{
    internal static class Program
    {
        /// <summary>
        ///     <c>Vion.Cli.Derive &lt;openapi&gt; &lt;asyncapi&gt; &lt;cli.json&gt; &lt;out dir&gt;</c>: reads the two documents
        ///     and the presentation file, writes <c>Operations.g.cs</c> and <c>Channels.g.cs</c> into the directory. Exit 1
        ///     on a refused input, 2 on a usage error.
        /// </summary>
        private static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("usage: Vion.Cli.Derive <openapi/v1.json> <asyncapi/v1.json> <cli.json> <out dir>");
                return 2;
            }

            try
            {
                var document = Deriver.Derive(File.ReadAllText(args[0]), File.ReadAllText(args[2]));
                var channels = Deriver.DeriveChannels(File.ReadAllText(args[1]), document);
                var target = Path.Combine(args[3], "Operations.g.cs");
                var channelsTarget = Path.Combine(args[3], "Channels.g.cs");
                Directory.CreateDirectory(args[3]);
                File.WriteAllText(target, Emitter.Emit(document), new UTF8Encoding(false));
                File.WriteAllText(channelsTarget, ChannelEmitter.Emit(channels), new UTF8Encoding(false));
                Console.WriteLine($"derive: {document.Operations.Count} operations, snapshot sha256 {document.SnapshotSha256} -> {target}");
                Console.WriteLine($"derive: {channels.Channels.Count} channels, snapshot sha256 {channels.SnapshotSha256} -> {channelsTarget}");
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