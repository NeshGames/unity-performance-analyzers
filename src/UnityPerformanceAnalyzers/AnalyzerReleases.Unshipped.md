; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA0009 | Performance | Disabled | List Count hoist; retired after Unity 6 IL2CPP measured only ~5.5 ns per 64-item loop against a large safety-analysis surface
UPA0021 | Performance | Disabled | magnitude/Distance rewrite; retired after Unity 6 IL2CPP measured only ~0.72 ns per comparison and the general rewrite retained a negative-threshold semantic caveat
UPA0022 | Performance | Disabled | Enum.HasFlag in per-frame method; retired after Unity 6 evidence disproved the performance premise
UPA1000 | Correctness | Disabled | Leaf class not sealed; retired after measured gain remained below noise
