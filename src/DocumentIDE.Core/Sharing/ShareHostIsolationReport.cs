using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.Sharing;

public record PathAccessResult(string Path, string AccessType, bool Allowed, string Notes);

public record IsolationMechanismEvaluation(
    string MechanismName, 
    bool FeasibleForV1, 
    string IsolationLevel, 
    string CompatibilityCost, 
    string TradeOffDetails
);

public class ShareHostIsolationReport
{
    public string IsolationPosture { get; set; } = string.Empty;
    public bool IsOsSandbox { get; set; }
    public bool IsProcessSeparated { get; set; }
    public string ShareRootPath { get; set; } = string.Empty;
    public bool IsShareRootAccessPermitted { get; set; }
    public bool IsNetworkBindingPermitted { get; set; }
    public string ResidualRisk { get; set; } = string.Empty;
    public List<PathAccessResult> TestedPaths { get; set; } = new();
    public List<IsolationMechanismEvaluation> EvaluatedMechanisms { get; set; } = new();
}
