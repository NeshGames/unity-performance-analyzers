; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA0001 | Performance | Warning | Component lookup in per-frame method
UPA0002 | Performance | Warning | Object name or tag accessed in per-frame method
UPA0003 | Performance | Warning | String-based shader or animator property access
UPA0004 | Performance | Warning | Instantiating accessor used in per-frame method
UPA0005 | Performance | Disabled | Direct UnityEngine.Debug logging call
UPA0006 | Performance | Warning | Reference type allocated in per-frame method
UPA0007 | Performance | Warning | Capturing lambda in per-frame method
UPA0008 | Performance | Warning | stackalloc inside a loop
UPA0009 | Performance | Disabled | List Count not hoisted out of for loop
UPA0010 | Performance | Warning | Raycast without explicit maxDistance or layerMask
UPA0011 | Performance | Disabled | SetActive used to toggle UI visibility
UPA0012 | Performance | Disabled | TextMeshPro text assignment instead of SetText
UPA0013 | Performance | Disabled | System.Linq usage in per-frame method
UPA0014 | Performance | Warning | Scene-search API call in per-frame method
UPA0015 | Performance | Info | Camera.main accessed in per-frame method
UPA0016 | Performance | Warning | SendMessage / BroadcastMessage call
UPA0017 | Performance | Warning | Array-returning GetComponents overload in per-frame method
UPA0018 | Performance | Warning | Allocating array-returning Unity API in per-frame method
UPA0019 | Performance | Warning | Boxed value yielded from coroutine
UPA0020 | Performance | Disabled | Lambda in WaitUntil / WaitWhile construction
UPA0021 | Performance | Warning | magnitude or Distance compared where sqrMagnitude suffices
UPA0022 | Performance | Warning | Enum.HasFlag in per-frame method
UPA0023 | Performance | Disabled | OnGUI declared in player code
UPA0024 | Performance | Disabled | Resources.Load in per-frame method
UPA0025 | Performance | Warning | Finalizer declared in runtime code
UPA0026 | Performance | Warning | Value type boxed by inherited method call
UPA1000 | Correctness | Disabled | Leaf class not sealed
UPA1001 | Correctness | Warning | Non-exhaustive enum switch
UPA2000 | Ecosystem | Disabled | String concatenation in per-frame method
UPA2010 | Ecosystem | Disabled | User-authored async method returns Task
UPA2011 | Ecosystem | Disabled | Coroutine IEnumerator Unity message or method
UPA2012 | Ecosystem | Disabled | Async void or unawaited fire-and-forget
UPA2021 | Ecosystem | Disabled | Public Action event used for observable state
UPA2030 | Ecosystem | Disabled | Tween created in per-frame method
UPA2031 | Ecosystem | Disabled | Discarded infinite tween without SetLink
UPA2032 | Ecosystem | Disabled | String tween ID
UPA3000 | Platform | Disabled | Threading API unsupported on WebGL
UPA3001 | Platform | Disabled | Sockets API unsupported on WebGL
UPA3002 | Platform | Disabled | Synchronous file IO unsupported on WebGL
UPA3003 | Platform | Disabled | Process API unsupported on WebGL
UPA3004 | Platform | Disabled | Blocking wait on asynchronous operation
