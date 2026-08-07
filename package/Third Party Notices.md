# Third Party Notices

This package does not redistribute any third-party software. The notices below
document relationships with third-party projects that this package's
configuration files reference.

## Microsoft.Unity.Analyzers

The severity presets shipped as samples of this package configure diagnostic IDs
(`UNT####`) defined by Microsoft.Unity.Analyzers. The analyzer itself is **not**
included in this package; those preset entries only take effect when
Microsoft.Unity.Analyzers is present in the consuming project (for example, the
copy bundled with Visual Studio Tools for Unity).

Microsoft.Unity.Analyzers is licensed under the MIT License:
https://github.com/microsoft/Microsoft.Unity.Analyzers/blob/main/LICENSE.md

Copyright (c) Microsoft Corporation.

## Referenced libraries (not redistributed)

Some rules in this package activate, or adapt their advice, based on whether the
consuming project references certain third-party libraries — currently UniTask,
ZString, and R3 (Cysharp), and DOTween (Demigiant). Detection is by assembly
name only. This package does **not** include, redistribute, or derive from any
code of these libraries; diagnostic messages and rule documentation merely
mention them by name and may link to their official documentation. Each library
remains governed by its own license in the consuming project.
