; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
UPA0021 | Performance | Disabled | Performance | Warning | magnitude or Distance compared where sqrMagnitude suffices - off until an IL2CPP measurement exists
UPA0031 | Performance | Info | Performance | Warning | Instantiate or Destroy in per-frame method - five findings on real games, none of them per-frame
