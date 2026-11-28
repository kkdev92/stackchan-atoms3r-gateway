# StackChan ATOMS3R Gateway project guide

Start here before taking action in this project. Read the section for your task: [reporting a problem](#reporting-a-problem), [verifying a change](#verifying-a-change), [development instructions](#development-instructions), or [security and private reports](#security-and-private-reports). Consult this guide again if your task changes.

## Reporting a problem

For gateway issues, include the SDK/host version, OS, architecture and device firmware version. If you tried fixed-response mode or the simulator, include the result. Remove tokens, model inputs, recordings and private deployment details. For the device itself, its HTTP API or the wire contract, use the [firmware issue tracker](https://github.com/kkdev92/stackchan-atoms3r/issues).

For an interface proposal, identify the affected firmware or SDK surface and whether additional hardware is needed.

Include the .NET SDK version, a package version or source commit, and the device firmware version from `device.describe`. Say which recognizer, model or synthesizer was involved. Redact endpoints and deployment names in startup logs. If you are unsure which repository owns the issue, you can open it in the gateway tracker.

## Working on the project

Use the development instructions below for the reference host, simulator and package layout. Record which end of the device/gateway boundary changed. Setup and verification details appear below.

## Verifying a change

Use the rows that apply to your change when preparing a merge candidate.

| Change | What to check |
| --- | --- |
| Build or dependency changes | From the repository root, run the SDK restore and format checks, build the local SDK packages and reference host, then check host formatting. Use the commands below in order. |
| Device communication or integration | Start the gateway in offline mode as described in the development instructions below. In a second terminal, configure the simulator with the matching token via `STACKCHAN_DEVICE_TOKEN`, then run `dotnet run --project tools/device-sim -- --scenario all`. Keep the token out of the PR. For device-specific behavior, record the physical hardware and firmware version tested. |
| SDK, architecture or user-visible behavior | Update XML docs for affected APIs, the relevant package README, architecture notes or Unreleased changelog as applicable. Explain any changed assertion. |

Run the build checks from the repository root:

```sh
dotnet restore src/sdk/StackChan.Gateway.Sdk.slnx --locked-mode
dotnet format src/sdk/StackChan.Gateway.Sdk.slnx --verify-no-changes --no-restore
pwsh eng/build-all.ps1 -Configuration Release
dotnet format src/app/StackChan.Gateway.App.slnx --verify-no-changes --no-restore
```

See the [shared contribution guidelines](https://github.com/kkdev92/.github/blob/main/CONTRIBUTING.md) for the two pull request options.

## Development instructions

The following development and verification requirements apply when preparing product changes for merge. Reference implementations only need a short description of the idea.

## Prerequisites

- The .NET SDK version in [`global.json`](global.json). It is a **pin**, not a floor:
  `rollForward: latestPatch` accepts a later patch in the same feature band and nothing else
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
artifacts/   Packed SDK packages. Created by pack-sdk.ps1; gitignored
```

`tools/` holds projects, `eng/` holds scripts and MSBuild fragments. That is the only reason the
PowerShell lives one directory up from `device-sim`.

The solution files live beside what they build: `src/sdk/StackChan.Gateway.Sdk.slnx` and
`src/app/StackChan.Gateway.App.slnx`. Test and tool projects are referenced from them with
`../../`.

`nuget.config` points a package source at `artifacts/`, and `packageSourceMapping` sends only
`Kkdev92.StackChan.*` there. The directory does not need to exist for a restore to succeed —
measured — so nothing in it is tracked.

## Checks

One command runs everything:

```bash
pwsh eng/build-all.ps1
```

It does three things in order, and stops at the first failure:

```text
1. dotnet test src/sdk/StackChan.Gateway.Sdk.slnx
2. pack-sdk.ps1                                     packages into artifacts
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

## Security and private reports

Follow this project’s private reporting instructions below. If its reporting form is unavailable and no other project contact is listed, use the [shared private contact](https://github.com/kkdev92/.github/blob/main/SECURITY.md#private-contact).

## Supported versions

Only `main` and the most recent release are supported. Fixes land on `main` and are
included in the next release; older versions are not maintained as separate supported lines.
All eight packages move together.

Which release that is comes from the NuGet badge in the readme and from the tags, not from
a number written out here: one in prose is correct on the day it is written and wrong on
the day the next one ships, with nothing failing in between.

## Reporting a vulnerability

Please use
[GitHub Private Vulnerability Reporting](https://github.com/kkdev92/stackchan-atoms3r-gateway/security/advisories/new)
to report security issues rather than opening a public issue.

If that form is unavailable, contact <me@kkdev92.dev> with `security` in the subject.

Do **not** include the device token, an API key, Wi-Fi credentials, a private address, or a
recording in a report. The startup configuration log replaces secret values with their lengths,
but still contains endpoints and model names. Review and redact it before attaching it.

This project is maintained on a best-effort basis. You can expect an acknowledgment and an
initial assessment when the report has been reviewed. Remediation timing depends on the impact
and the availability of a safe fix.

## Scope

This project covers the gateway side of a conversation with a device. Vulnerabilities in the
device firmware should usually be reported to
[stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r), and vulnerabilities in a
downstream service should usually be reported to that service. If you are unsure which project
owns a problem, report it privately here and we will help identify the appropriate destination.

In scope:

- A way to reach `POST /v1/converse` without the token, or a timing signal that narrows it
- A request shape that gets past the identifier, size or utterance checks
- A path that writes a token, an API key or conversation content to a log or a response
- Unbounded memory, CPU or file growth driven by input an attacker controls — including a
  response from a downstream service, which is untrusted input here
- A dependency advisory that `NuGetAudit` does not already surface

Out of scope:

- The gateway listening on `0.0.0.0`. This is the documented default; normal provider mode
  requires a token, and tokenless fixed-response mode requires an explicit LAN override
- Plain HTTP between device and gateway. See below; it is a firmware constraint
- Anything requiring physical access to the device or the host
- Fixed-response mode being unauthenticated on loopback. Startup rejects the equivalent LAN
  configuration unless it is explicitly allowed

## Security posture

The gateway is meant to run on a home network beside the device it serves. The threat it is built
against is another client on the same network — eavesdropping, or starting a conversation without
permission. It is not built against exposure to the internet, and should not be arranged that
way.

**A token is required.** Unless fixed-response mode is on, startup fails without
`StackChan:Atoms3R:Token`. The comparison uses a fixed-time primitive. For values with equal byte
lengths, comparison time does not depend on which bytes match; request length remains observable.

**Tokenless LAN exposure is blocked by default.** If a tokenless gateway would listen on a
non-loopback address, startup stops rather than continuing.
`StackChan:Security:AllowUnauthenticatedLan=true` is the only way through, and setting it is a
statement of intent. The listen address is not quietly moved to loopback; the configured address
remains visible and predictable.

**Sensitive content is excluded from the gateway's structured logs.** Tokens and API keys are
represented by their length, transcripts and replies are logged by length, and the weather HTTP
logger removes query strings. Review logs before sharing them because endpoints, model names,
exception types, and other deployment details remain visible.

**Untrusted input is bounded** on both sides. From the device: request body (2 MiB by default),
identifier length and character set, and the utterance cap. From a downstream service: every
response is read under a byte limit rather than buffered freely, a tool call parsed out of body
text is length-capped and count-capped, and a WAV with a malformed chunk length is rejected instead
of looped over.

**Capability and synthesis failures have fallbacks.** `CapabilityCall.AnswerAsync` returns a
configured spoken message after a capability failure, and a synthesis failure leaves the sentence
available as text. Other provider failures are reported through protocol error events.

**The gateway does not persist audio.** Code in this repository writes a recording to disk only
when `device-sim --save` is used explicitly. Downstream services may retain audio under their own
policies. `.gitignore` excludes `*.wav`, `*.mp3` and `*.pcm` from normal Git additions.

## Transport is not encrypted — action required

The device firmware does not implement TLS, so `POST /v1/converse` is plain HTTP. The audio, the
transcript, the reply and the token are all visible to anything that can observe the local
network. The token travels in a header on every request.

This is a property of the device, not a setting on this side, and no configuration here changes
it. Use a network you control, do not expose the gateway to the internet, and treat the token as
something that a network observer already has.

## Secrets and where they live

|  |  |
| --- | --- |
| Device token | `StackChan__Atoms3R__Token`, or `start-gateway.ps1 -Token` |
| Weather API key | `StackChan__Weather__ApiKey` |
| Model endpoint key | `StackChan__Agent__ApiKey`. A placeholder for a local endpoint |

`appsettings.json` ships with empty strings where secrets go. Do not fill them in; it is a tracked
file, so Git ignore rules cannot protect secrets added to it.

The weather service receives its API key in the query string. The gateway replaces the standard
HTTP logger for that client with one that always removes the query, even when
`System.Net.Http.DisableUriRedaction=true`.

## Not a compliance guarantee

This software records audio around the device and sends it to services selected by the operator.
Operators are responsible for evaluating consent, retention, downstream handling of recordings,
and terms that apply to synthesized voices in their deployment. This gateway does not by itself
provide compliance or licensing assurances.
