# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-08-07

### Added

- Initial rule set UPA0001–UPA0012, UPA1000, UPA1001 (performance and correctness rules).
- Ecosystem rules UPA2000, UPA2001, UPA2010, UPA2011, UPA2012, UPA2021 — advice and
  activation adapt to referenced packages (UniTask, ZString, R3), and UPA2000/2001
  report on per-frame hot paths only.
- WebGL platform rules UPA3000–UPA3003, active when `UPA_TARGET_WEBGL` is defined.
- Severity presets (minimal / recommended / strict / cysharp-stack, plus the webgl-addon
  and editor-relaxed rulesets) with `.editorconfig` variants for IDE parity, and a
  Smoke Test sample.
- Hot-path detection configurable via `upa_hot_path_*` options (IDE analysis only).
- Every diagnostic carries a `snippet` property for stable violation identification in
  downstream tooling.
- Documentation in English and Traditional Chinese.
