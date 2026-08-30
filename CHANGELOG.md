# Changelog

Notable user-visible changes, in the spirit of
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), with versions following
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) from `1.0.0` onward.

The `0.x` line is pre-release and breaking changes are expected. All eight
`Kkdev92.StackChan.*` packages are released together at the same version; the version below is
the one in `VersionPrefix`, and the release tag is a copy of it.

The device firmware version is a separate axis. A firmware release does not appear here unless it
changes something on this side.

## [Unreleased]

## [0.1.0] - 2026-08-28

First release. Not yet published to NuGet.org.

### Added

- **`Kkdev92.StackChan.Gateway.Abstractions`** — the contracts, with no dependencies of any kind.
  Six interfaces, all of them extension points: `ISpeechToText`, `ITextToSpeech`, `IAgent`,
  `ITurnRuntime`, `ISessionRegistry`, `ICapability`. Also the device limits and the telemetry
  definitions
- **`Kkdev92.StackChan.Gateway.Runtime`** — turn orchestration with a deadline, one turn per
  session, an admission gate, idle eviction, sentence splitting, expression markers and text
  shaping for speech. Knows nothing about HTTP or SSE
- **`Kkdev92.StackChan.Gateway.Protocol.Atoms3R`** — the wire contract. SSE envelopes, base64
  PCM, `seq` numbering, header validation and `POST /v1/converse`
- **`Kkdev92.StackChan.Gateway.AgentFramework`** — an `IAgent` backed by Microsoft Agent Framework
  against an OpenAI-compatible endpoint, with capabilities projected as tools. Includes three
  layers for small local models: parsing tool calls that arrive as body text, running a capability
  ahead of time on a trigger word, and collapsing type arrays on optional parameters
- **`Kkdev92.StackChan.Gateway.Providers`** — WAV parsing and writing, resampling to 16 kHz,
  non-speech annotation stripping, endpoint validation, a response size cap and a circuit breaker
- **`Kkdev92.StackChan.Gateway.Capabilities`** — `CapabilityCall.AnswerAsync`, which returns words
  instead of throwing, culture-independent number formatting for speech, and endpoint validation
- **`Kkdev92.StackChan.Gateway.Diagnostics`** — fixed responses for recognition, the agent and
  synthesis, so a device can be brought up with no downstream service at all
- **`Kkdev92.StackChan.Gateway.TestKit`** — 13 conformance checks against raw wire bytes, SSE
  reassembly, device-shaped requests and hand-written fakes. Depends only on `Abstractions`, with
  no test framework
- **Reference host** (`src/app`, not published) — whisper.cpp and Piper-plus with VOICEVOX
  providers, time and weather capabilities, three health endpoints, a startup configuration dump,
  and a refusal to start when an unauthenticated gateway would be exposed to the LAN
- **`tools/device-sim`** (not published) — drives the wire without a device across repeated
  turns, concurrency, disconnects, long input, and malformed requests, including eight refusal
  cases with their expected status and message

### Notes

- Targets `net10.0` only, built with C# 14. Packages carry SourceLink and a separate `.snupkg`
- The wire contract is defined by
  [stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r). `TestKit` allows a host to
  verify that it conforms
- Verified on real hardware. Regression coverage includes admission ordering, malformed WAV
  chunk lengths, and parser complexity

[Unreleased]: https://github.com/kkdev92/stackchan-atoms3r-gateway/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kkdev92/stackchan-atoms3r-gateway/releases/tag/v0.1.0
