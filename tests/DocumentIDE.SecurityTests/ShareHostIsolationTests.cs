using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using DocumentIDE.Core.Sharing;
using Xunit;

namespace DocumentIDE.SecurityTests;

public class ShareHostIsolationTests
{
    private readonly ShareHostIsolationEvaluator _evaluator;
    private readonly ShareHostAclHardener _aclHardener;

    public ShareHostIsolationTests()
    {
        _evaluator = new ShareHostIsolationEvaluator();
        _aclHardener = new ShareHostAclHardener();
    }

    [Fact]
    public void ExperimentEvidence_IsRecorded_And_AccuratelyReflectsIsolationPosture()
    {
        // Act
        var report = _evaluator.EvaluateIsolationPosture();

        // Assert - Evidence recorded
        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report.IsolationPosture));
        Assert.False(string.IsNullOrWhiteSpace(report.ResidualRisk));
        Assert.True(report.IsProcessSeparated, "ShareHost MUST run as a separate process");
        Assert.False(report.IsOsSandbox, "ShareHost MUST NOT claim to be an OS sandbox in V1 baseline");

        // Assert - Evaluated mechanisms exist
        Assert.NotNull(report.EvaluatedMechanisms);
        Assert.NotEmpty(report.EvaluatedMechanisms);

        var appContainerEval = report.EvaluatedMechanisms.FirstOrDefault(m => m.MechanismName == "AppContainer");
        Assert.NotNull(appContainerEval);
        Assert.False(appContainerEval.FeasibleForV1, "AppContainer isolation is evaluated as not feasible for V1 Win32 deployment");

        var restrictedTokenEval = report.EvaluatedMechanisms.FirstOrDefault(m => m.MechanismName == "RestrictedToken");
        Assert.NotNull(restrictedTokenEval);
        Assert.False(restrictedTokenEval.FeasibleForV1, "RestrictedToken isolation is evaluated as not feasible for V1 due to user file read access");

        var aclEval = report.EvaluatedMechanisms.FirstOrDefault(m => m.MechanismName == "DirectoryACLs");
        Assert.NotNull(aclEval);
        Assert.True(aclEval.FeasibleForV1);

        var procSepEval = report.EvaluatedMechanisms.FirstOrDefault(m => m.MechanismName == "ProcessSeparation");
        Assert.NotNull(procSepEval);
        Assert.True(procSepEval.FeasibleForV1);
    }

    [Fact]
    public void ShareHostDocs_DoNotClaimSandboxing_UnlessProved()
    {
        var report = _evaluator.EvaluateIsolationPosture();

        // If IsOsSandbox is false, ResidualRisk MUST explicitly state process separation is not an OS sandbox
        Assert.False(report.IsOsSandbox);
        Assert.Contains("process separation", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not an OS sandbox", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arbitrary local", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChosenRestrictionPosture_PermitsShareArtifactReads_AndNetworkBinding()
    {
        var tempShareDir = Path.Combine(Path.GetTempPath(), $"share_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempShareDir);
            var report = _evaluator.EvaluateIsolationPosture(tempShareDir);

            Assert.True(report.IsShareRootAccessPermitted, "ShareHost MUST be able to read/write its own share artifact root");
            Assert.True(report.IsNetworkBindingPermitted, "ShareHost MUST be able to bind to loopback network interface");
        }
        finally
        {
            if (Directory.Exists(tempShareDir))
            {
                try { Directory.Delete(tempShareDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void FilesystemProbe_DocumentsResidualRisk_OfUnrestrictedUserReads()
    {
        var report = _evaluator.EvaluateIsolationPosture();

        Assert.NotNull(report.TestedPaths);
        Assert.NotEmpty(report.TestedPaths);

        // Share root should be allowed
        var shareRootResult = report.TestedPaths.FirstOrDefault(p => p.Path.Contains("shares") || p.Path.Contains("DocumentIDE"));
        Assert.NotNull(shareRootResult);
        Assert.True(shareRootResult.Allowed);

        // User profile / Temp should be probed and recorded
        var tempResult = report.TestedPaths.FirstOrDefault(p => p.Path.Equals(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) || p.Path.Contains("Temp"));
        Assert.NotNull(tempResult);

        // Ensure notes document the risk that process separation running as user allows reading user-readable paths
        Assert.True(report.TestedPaths.Any(p => !string.IsNullOrWhiteSpace(p.Notes)));
    }

    [Fact]
    public void ShareArtifactRoot_ACLHardening_CanBeAppliedAndVerified()
    {
        var tempShareDir = Path.Combine(Path.GetTempPath(), $"acl_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempShareDir);

            bool applied = _aclHardener.ApplyShareArtifactDirectoryAcl(tempShareDir);
            Assert.True(applied, "ACL hardening should execute successfully");

            bool verified = _aclHardener.VerifyShareArtifactDirectoryAcl(tempShareDir);
            Assert.True(verified, "ACL hardening verification should succeed");
        }
        finally
        {
            if (Directory.Exists(tempShareDir))
            {
                try { Directory.Delete(tempShareDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void AppContainer_And_RestrictedToken_FeasibilityAssessed()
    {
        var report = _evaluator.EvaluateIsolationPosture();

        Assert.NotNull(report.EvaluatedMechanisms);
        Assert.True(report.EvaluatedMechanisms.Count >= 4, "Must evaluate at least AppContainer, RestrictedToken, DirectoryACLs, and ProcessSeparation");

        foreach (var mechanism in report.EvaluatedMechanisms)
        {
            Assert.False(string.IsNullOrWhiteSpace(mechanism.MechanismName));
            Assert.False(string.IsNullOrWhiteSpace(mechanism.IsolationLevel));
            Assert.False(string.IsNullOrWhiteSpace(mechanism.CompatibilityCost));
            Assert.False(string.IsNullOrWhiteSpace(mechanism.TradeOffDetails));
        }
    }
}
