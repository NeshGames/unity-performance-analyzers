# Security policy

## Supported versions

The most recent release receives fixes. Before 1.0, that is the only version supported —
see [versioning and rule governance](docs/versioning.md).

## Reporting a vulnerability

Use GitHub's **Report a vulnerability** button on this repository's Security tab. It opens
a private advisory that only the maintainers can read; please do not open a public issue
for something exploitable.

Expect an acknowledgement within a week. If a fix is warranted it ships as a patch release
with the advisory published alongside it.

## What is in scope

This package is a set of Roslyn analyzers and a command-line tool. The realistic attack
surface is small but not empty:

- **The command-line tool reads paths you give it** — response files (`@args.rsp`),
  baselines, rulesets, `.editorconfig` files and source files. A crafted file that makes
  the tool write outside the directory it was pointed at, or read something it was not
  asked for, is a vulnerability.
- **The analyzers run inside your compiler.** They do no file IO beyond the additional
  files Roslyn hands them, hold no mutable static state, and open no network connections.
  Anything that contradicts that is a vulnerability, not a design choice.
- **The Editor window writes into your project** — rulesets, `.editorconfig` and the
  options file. Writing outside the project, or to a path derived from untrusted input,
  is in scope.

## What is not in scope

- A rule that fails to report something — that is a bug, and the [contribution
  guide](CONTRIBUTING.md) covers it
- A rule that reports something it should not — likewise a bug, and the most useful kind
- Anything requiring an attacker who can already run code on the developer's machine or
  edit the repository being compiled
