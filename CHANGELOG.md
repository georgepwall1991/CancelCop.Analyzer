# Changelog

All notable changes to CancelCop.Analyzer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **CC001 code fix** now produces compilable, clean output in three previously broken cases:
  - inserts the `CancellationToken` parameter *before* a trailing `params` parameter
    instead of after it (was emitting CS0231);
  - picks a non-colliding parameter name (`ct`, then `cancellationToken2`, …) when a
    parameter named `cancellationToken` already exists (was emitting CS0100);
  - inserts `using System.Threading;` without leaving a spurious blank line between
    using directives (the malformed output was previously baked into the tests).

### Changed

- CI now installs both the .NET 9 and .NET 10 SDKs and `global.json` is pinned to
  `10.0.300`, so the `net10.0` projects build deterministically in CI (was failing
  with NETSDK1045).
