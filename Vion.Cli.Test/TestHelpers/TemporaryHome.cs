using System;
using System.IO;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>A home directory of the test's own, so no test reads or writes the developer's `~/.vion`.</summary>
    internal sealed class TemporaryHome : IDisposable
    {
        public string Root { get; }

        public TemporaryHome()
        {
            Root = Path.Combine(Path.GetTempPath(), "vion-cli-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                // A handle still open on Windows; the temp directory is cleaned by the system.
            }
        }

        /// <summary>Writes a file under the home and returns its path, for an `--input` or `--file` the test needs on disk.</summary>
        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }
    }
}