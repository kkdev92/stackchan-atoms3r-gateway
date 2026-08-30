# Contributing

Thank you for helping improve Kkdev92.StackChan.Gateway. Bug fixes, documentation updates,
tests, and focused feature work are welcome. Draft pull requests are also welcome when you
would like early feedback. Opening an issue before a larger change is appreciated because it
helps confirm the scope and avoid duplicate work.

## Prerequisites

- The .NET SDK version in [`global.json`](global.json). It is a **pin**, not a floor:
  `rollForward: latestPatch` accepts a later patch of `10.0.3xx` and nothing else
- PowerShell 7 (`pwsh`). The scripts are cross-platform
- Git on Windows configured so that line endings are not rewritten (see below)

```bash
git clone https://github.com/kkdev92/stackchan-atoms3r-gateway.git
cd stackchan-atoms3r-gateway
pwsh eng/build-all.ps1
```

There is no plain `dotnet build` at the root, and no separate restore step. The reference host
consumes the SDK as packages from a local feed, so `build-all.ps1` has to run at least once
before `src/app/StackChan.Gateway.App.slnx` will restore. That is deliberate: a project
reference resolves what the compiler can see, while a package reference resolves what actually
got packed, so a packaging defect fails here rather than at a consumer.

The build treats warnings as errors. A pull request that introduces a warning does not pass.

## Line endings matter here

Everything in this repository is LF, pinned by [`.gitattributes`](.gitattributes) with
`* text=auto eol=lf`, and `.editorconfig` says the same.

Git on Windows defaults to `core.autocrlf=true`. Without those rules a checkout produces CRLF,
`dotnet format` then reports `ENDOFLINE` on every file it touches, and the Windows and Linux CI
legs disagree with each other. These repository settings need to remain in place.

NuGet writes `packages.lock.json` with CRLF when it regenerates one. Git normalizes it on commit,
so it is not a problem in the index, but if `dotnet format` starts complaining after a restore
that is why.

## Repository layout

```text
src/sdk/     Eight packages. IsPackable=true. This is what ships
src/app/     The reference host. IsPackable=false. Zero ProjectReference to src/sdk
tests/sdk/   Tests for the packages
tests/app/   Tests for the reference host, run against the packed packages
tools/       Projects that are not shipped. device-sim drives the wire without a device
eng/         The build and release machinery: scripts, and the shared package-README footer
docs/        Architecture, configuration, operations
.config/     Local dotnet tool manifest. `dotnet tool restore` before using dotnet-counters
local-nuget/ The local feed. Contents are gitignored; .gitkeep is not
```

`tools/` holds projects, `eng/` holds scripts and MSBuild fragments. That is the only reason the
PowerShell lives one directory up from `device-sim`.

The solution files live beside what they build: `src/sdk/StackChan.Gateway.Sdk.slnx` and
`src/app/StackChan.Gateway.App.slnx`. Test and tool projects are referenced from them with
`../../`.

`local-nuget/.gitkeep` has to stay tracked. `nuget.config` points a package source at that
directory and restore fails with NU1301 if it does not exist.

## Checks

One command runs everything:

```bash
pwsh eng/build-all.ps1
```

It does three things in order, and stops at the first failure:

```text
1. dotnet test src/sdk/StackChan.Gateway.Sdk.slnx
2. pack-sdk.ps1                                     packages into local-nuget
3. dotnet test src/app/StackChan.Gateway.App.slnx   against those packages
```

Step 3 verifies the package contents. A project reference resolves what the compiler can see,
while a package reference resolves what was actually packed. A type left `internal`, a dependency
missing from the `.nuspec`, or a project that does not pack can pass step 1 but fail step 3.

Additional checks, all of which CI also runs:

```bash
dotnet format src/sdk/StackChan.Gateway.Sdk.slnx --verify-no-changes --no-restore
dotnet restore src/sdk/StackChan.Gateway.Sdk.slnx --locked-mode
pwsh eng/check-version.ps1
dotnet run --project tools/device-sim -- --scenario all
```

`device-sim` needs a gateway to talk to. Start one with no downstream services first:

```bash
pwsh eng/start-gateway.ps1 -Offline -Token <32-char-token>
```

## Design requirements

These requirements keep package boundaries and the wire contract consistent. Tests enforce
them so that violations are reported during the build.

| Rule | Enforced by | Why |
| --- | --- | --- |
| `Abstractions` has no `PackageReference`, `ProjectReference` or `FrameworkReference` | `ArchitectureInvariantTests` | Every other package sits on it. A dependency here reaches all of them |
| Every other package depends only on `Abstractions` | `ArchitectureInvariantTests` | Same |
| `HttpContext` and ASP.NET types never reach `Runtime` | `ArchitectureInvariantTests`, 11 forbidden directions in all | The wire shape leaking into orchestration is what the split exists to prevent |
| `Runtime` never sees which agent framework is in use | `ArchitectureInvariantTests` | `IAgent` is the seam. Replacing the agent must not mean touching the runtime |
| The reference host holds zero `ProjectReference` to the SDK | `AppCompositionTests` | The host builds against what actually got packed, so a packaging defect fails here rather than at a consumer |
| Every conformance check can detect a violation | Mutation tests with malformed streams | A check that cannot fail reports success either way |
| Every project is covered by `NuGetAudit` | `Directory.Solution.targets`, in CI | A project skipped for any reason is reported as *not audited*, which reads like a clean result |
| The eight package READMEs end with the shared footer | `ArchitectureInvariantTests` | A NuGet.org page is often the first package documentation a user sees. The source is `eng/PACKAGE-README-FOOTER.md` |
| A new package under `src/sdk` includes its rationale in the pull request | review | Keeping the package graph small is an explicit design goal |

If a proposal requires revisiting one of these requirements, please open a design discussion
before changing the test that enforces it.

Beyond those:

- Analysis runs at `latest-recommended`. Suppressions go in `Directory.Build.props` with the
  reason written out, not inline
- Nullable reference types are on everywhere
- Public API on the SDK gets an XML doc comment. `GenerateDocumentationFile` is on, so a missing
  one is a warning, which is an error
- Document a limit or threshold with the measurement or rationale that justified it, in a
  nearby comment

## Tests

| Suite | Purpose |
| --- | --- |
| `Kkdev92.StackChan.Gateway.Runtime.Tests` | Turn orchestration, sessions, sentence assembly, text shaping |
| `Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Tests` | The wire, byte for byte, plus request validation and refusals |
| `Kkdev92.StackChan.Gateway.Conformance.Tests` | The 13 checks, the 25 mutations, and the architecture rules |
| `Kkdev92.StackChan.Gateway.AgentFramework.Tests` | Tool-call parsing, prefetch, and parser limits for malformed input |
| `Kkdev92.StackChan.Gateway.Providers.Tests` | WAV robustness, the circuit breaker, response caps |
| `Kkdev92.StackChan.Gateway.Capabilities.Tests` | Spoken fallback behavior when a capability fails |
| `StackChan.Gateway.App.Tests` | The composed host, against the packed packages |
| `StackChan.Provider.*.Tests`, `StackChan.Capability.*.Tests` | The reference host's providers and capabilities |

### Test expectations

- A behavior change should include a test that fails without the change
- If an existing assertion needs to change, explain the reason in the pull request
- Prefer testing through the public surface. The conformance checks read raw bytes for this reason
- Record model-specific behavior with the model name, version, and observation date. Include a
  test that pins the relevant request or response shape
- Generated input is preferred when testing parsers and other code that accepts untrusted input.
  The parser limits and WAV robustness suites provide examples
- Test names describe what should happen. Underscores are fine — `CA1707` is suppressed for test
  projects only

Hardware tests are not available in CI. If you tested on a device, say which firmware version.
`tools/device-sim` covers wire behavior that does not require the robot.

## Documentation

- [`docs/architecture.md`](docs/architecture.md) covers the layout and the reasoning. Update it
  when the layout changes
- [`docs/configuration.md`](docs/configuration.md) has every setting. Document the default and its
  rationale when adding a setting
- Each package has a `README.md` that becomes its NuGet.org page. Keep its code samples compiling
  and its API description current
- The [firmware repository](https://github.com/kkdev92/stackchan-atoms3r) is authoritative for the
  device wire contract. Link to the relevant firmware documentation when a gateway document
  repeats a wire-level value

## Pull requests

- Please keep a pull request focused on one concern
- Describe what you verified and how. Output from `build-all.ps1` is useful evidence
- Keep formatting-only changes separate from behavior changes when practical
- If a check is unavailable in your environment, describe what you ran and what remains. A
  maintainer can complete environment-specific verification

## Maintainer release process

`VersionPrefix` in `src/sdk/Directory.Build.props` is the only authority on the version. The eight
`Kkdev92.StackChan.*` entries in `Directory.Packages.props` are copies of it and must move in the
same commit, because the reference host resolves the SDK through them.

1. Pull request: raise `VersionPrefix` and the eight entries, add the `CHANGELOG.md` section, and
   update the README status line. Merge it.
2. Dispatch `release.yml` by hand. A manual run is a dry run: it builds, packs and stops before
   the push. Tags are what people check out, so a pipeline that fails after the tag exists costs a
   version number.
3. Run the hardware check. Flash the device, start the gateway in fixed-response mode, and have a
   conversation. This check covers the microphone, speaker, and end-to-end latency, which CI does
   not exercise.
4. Tag `vX.Y.Z` at that commit and push it.
5. Approve the `release` environment.

`check-version.ps1` runs in both CI and the release workflow, and compares the tag against
`VersionPrefix`, the eight central entries and the `CHANGELOG.md` heading. A tag from a commit
with mismatched versions fails there rather than reaching NuGet.org, which does not allow a
version to be replaced.

All eight packages are released together at the same version. Releasing one alone would leave the
others pointing at an older contract.

By contributing, you agree that your contribution is licensed under this repository's
[MIT License](LICENSE).
