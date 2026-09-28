; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA0022 | Performance | Disabled | Enum.HasFlag in per-frame method; retired after Unity 6 evidence disproved the performance premise
UPA1000 | Correctness | Disabled | Leaf class not sealed; retired after measured gain remained below noise
