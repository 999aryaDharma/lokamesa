using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DocumentIDE.Core.Sharing;

public class ShareHostAclHardener
{
    /// <summary>
    /// Applies restrictive ACLs to the share artifact directory to limit access.
    /// For the purpose of the sprint, it ensures the current user has full control
    /// and removes inherited permissions.
    /// </summary>
    /// <param name="sharesDirectoryPath">The root path of the share artifact directory.</param>
    /// <returns>True if ACLs were successfully applied.</returns>
    public bool ApplyShareArtifactDirectoryAcl(string sharesDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(sharesDirectoryPath))
            return false;

        try
        {
            // Ensure the directory exists
            Directory.CreateDirectory(sharesDirectoryPath);

            var dirInfo = new DirectoryInfo(sharesDirectoryPath);
            var security = dirInfo.GetAccessControl(AccessControlSections.Access);

            // Remove inherited rules
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Grant current user full control
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                var rule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);
                security.AddAccessRule(rule);
            }

            dirInfo.SetAccessControl(security);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that the ACLs set by <see cref="ApplyShareArtifactDirectoryAcl"/> are present.
    /// Checks for at least one Allow rule for the current user with FullControl.
    /// </summary>
    /// <param name="sharesDirectoryPath">The directory to verify.</param>
    /// <returns>True if verification succeeds.</returns>
    public bool VerifyShareArtifactDirectoryAcl(string sharesDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(sharesDirectoryPath))
            return false;
        try
        {
            var dirInfo = new DirectoryInfo(sharesDirectoryPath);
            var security = dirInfo.GetAccessControl(AccessControlSections.Access);
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null) return false;

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference.Equals(currentUser) &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.FullControl) &&
                    rule.AccessControlType == AccessControlType.Allow)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
