using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     How the store's files reach disk: written under a name of their own beside the target and moved over it, so
    ///     a reader never sees a half file and two processes never share a temporary; a secret-bearing file is created
    ///     owner-only, never widened for an instant.
    /// </summary>
    internal static class Files
    {
        private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private const UnixFileMode OwnerDirectory = OwnerFile | UnixFileMode.UserExecute;

        public static void WriteAtomically(string path, string text, bool ownerOnly)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (ownerOnly && !OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory, OwnerDirectory);
            }
            else
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (ownerOnly && !OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = OwnerFile;
            }

            using (var writer = new StreamWriter(new FileStream(temporary, options), new UTF8Encoding(false)))
            {
                writer.Write(text);
            }

            if (ownerOnly && OperatingSystem.IsWindows())
            {
                RestrictToOwner(temporary);
            }

            File.Move(temporary, path, true);
        }

        /// <summary>
        ///     On Windows an access list of one rule, the current user's, with inheritance off — the equivalent of the
        ///     Unix mode the file was created with, which dale's store leaves to the default list.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void RestrictToOwner(string path)
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRule(rule);
            }

            var owner = WindowsIdentity.GetCurrent().User!;
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            file.SetAccessControl(security);
        }
    }
}