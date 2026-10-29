using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     How the store's files reach disk: written beside the target and moved over it, so a reader never sees a half
    ///     file; a secret-bearing file is made owner-only before it takes the target's name.
    /// </summary>
    internal static class Files
    {
        public static void WriteAtomically(string path, string text, bool ownerOnly)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (ownerOnly)
            {
                RestrictToOwner(temporary);
            }

            File.Move(temporary, path, true);
        }

        /// <summary>
        ///     Mode 0600 on Unix; on Windows an access list of one rule, the current user's, with inheritance off — the
        ///     equivalent dale's store leaves to the default list.
        /// </summary>
        private static void RestrictToOwner(string path)
        {
            if (System.OperatingSystem.IsWindows())
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
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }
}