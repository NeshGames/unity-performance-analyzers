namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Agent-facing policy classification for every live or retired UPA rule.
/// This is deliberately orthogonal to severity: presets decide how loudly a policy class reports.
/// AF-09 may fold this table into the final canonical rule-definition source.
/// </summary>
public static class RulePolicyTable
{
    public enum Policy
    {
        Core,
        Optional,
        House,
        Platform,
        Retired,
    }

    public sealed record Row(string Id, Policy Kind);

    public static readonly Row[] Rows =
    {
        new("UPA0001", Policy.Core),
        new("UPA0002", Policy.Core),
        new("UPA0003", Policy.Core),
        new("UPA0004", Policy.Core),
        new("UPA0005", Policy.House),
        new("UPA0006", Policy.Core),
        new("UPA0007", Policy.Core),
        new("UPA0008", Policy.Core),
        new("UPA0009", Policy.Retired),
        new("UPA0010", Policy.Optional),
        new("UPA0011", Policy.Retired),
        new("UPA0012", Policy.Optional),
        new("UPA0013", Policy.Optional),
        new("UPA0014", Policy.Core),
        new("UPA0015", Policy.Optional),
        new("UPA0016", Policy.Core),
        new("UPA0017", Policy.Core),
        new("UPA0018", Policy.Core),
        new("UPA0019", Policy.Core),
        new("UPA0020", Policy.Optional),
        new("UPA0021", Policy.Retired),
        new("UPA0022", Policy.Retired),
        new("UPA0023", Policy.Optional),
        new("UPA0024", Policy.Optional),
        new("UPA0025", Policy.Core),
        new("UPA0026", Policy.Core),
        new("UPA0027", Policy.Core),
        new("UPA0028", Policy.Core),
        new("UPA0029", Policy.Optional),
        new("UPA0030", Policy.Core),
        new("UPA0031", Policy.Optional),
        new("UPA1000", Policy.Retired),
        new("UPA1001", Policy.Core),
        new("UPA2000", Policy.Core),
        new("UPA2001", Policy.Retired),
        new("UPA2010", Policy.House),
        new("UPA2011", Policy.House),
        new("UPA2012", Policy.Core),
        new("UPA2021", Policy.House),
        new("UPA2030", Policy.Core),
        new("UPA2031", Policy.Core),
        new("UPA2032", Policy.Retired),
        new("UPA3000", Policy.Platform),
        new("UPA3001", Policy.Platform),
        new("UPA3002", Policy.Platform),
        new("UPA3003", Policy.Platform),
        new("UPA3004", Policy.Platform),
    };
}
