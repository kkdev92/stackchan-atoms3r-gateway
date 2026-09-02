# Kkdev92.StackChan.Gateway

[![NuGet](https://img.shields.io/nuget/v/Kkdev92.StackChan.Gateway.Abstractions?label=nuget%20%28Abstractions%29)](https://www.nuget.org/packages/Kkdev92.StackChan.Gateway.Abstractions)
[![CI](https://github.com/kkdev92/stackchan-atoms3r-gateway/actions/workflows/ci.yml/badge.svg)](https://github.com/kkdev92/stackchan-atoms3r-gateway/actions)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/14383/badge)](https://www.bestpractices.dev/projects/14383)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

Eight .NET packages for building the conversational gateway that an
[AtomS3R stack-chan](https://github.com/kkdev92/stackchan-atoms3r) talks to. The device
records three seconds of audio, posts it, and plays the reply back sentence by sentence as it
arrives; everything between those two points is what these packages build.
_Designed for a robot on a local network, with support for locally hosted models._

```text
Fixed firmware, extensible gateway
```

> **Status:** Pre-release, published on NuGet.org. The wire format targets
> [stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r) `v0.1.0` or later. Public SDK
> APIs may change before `1.0.0`.

---

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Why Kkdev92.StackChan.Gateway](#why-kkdev92stackchangateway)
- [Usage](#usage)
- [Properties Checked in CI](#properties-checked-in-ci)
- [Known Limitations](#known-limitations)
- [How It Works](#how-it-works)
- [Platform Requirements](#platform-requirements)
- [Security and Privacy](#security-and-privacy)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Support & Maintenance Policy](#support--maintenance-policy)
- [License](#license)
- [Acknowledgments](#acknowledgments)

---

## Features

- **Incremental speech**: synthesis starts after the first complete sentence, allowing playback
  to begin while the model continues generating text
- **Offline diagnostics**: `Diagnostics` replaces recognition, the model, and synthesis with
  fixed components so the device connection can be tested independently
- **Firmware-compatible wire format**: the implementation preserves the firmware's event names,
  8,192-byte SSE event limit, 4,096-byte PCM chunks, and 16 kHz signed 16-bit mono format
- **Reusable conformance checks**: `TestKit` validates raw SSE bytes and can be used by hosts
  outside this repository
- **Small local model adapters**: compatibility layers handle tool calls returned as body text,
  endpoints that ignore `tool_choice: auto`, and optional parameters represented as type arrays
- **Replaceable components**: the interfaces in `Abstractions` are extension points, and the
  reference host consumes the SDK as packages without using `ProjectReference`
- **Spoken fallback behavior**: `CapabilityCall.AnswerAsync` returns a configured message after a
  capability failure, and synthesis failures preserve the sentence as text
- **Bounded resource use**: request bodies, identifier lengths, downstream response sizes,
  concurrent turns, and session counts have configurable limits; a circuit breaker pauses calls
  to repeatedly failing services

---

## Installation

```bash
dotnet add package Kkdev92.StackChan.Gateway.Abstractions
dotnet add package Kkdev92.StackChan.Gateway.Runtime
dotnet add package Kkdev92.StackChan.Gateway.Protocol.Atoms3R
```

| Package | Purpose |
| --- | --- |
| `Kkdev92.StackChan.Gateway.Abstractions` | The contracts. No NuGet package dependencies |
| `Kkdev92.StackChan.Gateway.Runtime` | Turn orchestration without HTTP or SSE dependencies |
| `Kkdev92.StackChan.Gateway.Protocol.Atoms3R` | The wire contract with the device |
| `Kkdev92.StackChan.Gateway.AgentFramework` | An `IAgent` backed by Microsoft Agent Framework |
| `Kkdev92.StackChan.Gateway.Providers` | Shared parts for writing a speech provider |
| `Kkdev92.StackChan.Gateway.Capabilities` | Shared parts for writing a capability |
| `Kkdev92.StackChan.Gateway.Diagnostics` | Fixed components for testing a device connection |
| `Kkdev92.StackChan.Gateway.TestKit` | Wire-protocol conformance checks and test fakes |

Take only what you need. Within this SDK, every package depends only on `Abstractions`; there is
no meta-package. A capability author references `Abstractions` alone.

---

## Quick Start

Bring the link up first, with no model and no recognizer:

```bash
git clone https://github.com/kkdev92/stackchan-atoms3r-gateway.git
cd stackchan-atoms3r-gateway
pwsh eng/build-all.ps1
pwsh eng/start-gateway.ps1 -Offline -Token <32-char-token>
```

The gateway listens on `http://0.0.0.0:8787`. Configure the device with that host address and the
same token, then press the button. The robot should speak a fixed sentence without the recognizer,
model, or synthesizer running.

Then wire the real services and require a token:

```bash
pwsh eng/start-gateway.ps1 -Token <32-char-token>
```

To build your own host instead:

```csharp
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R;
using Kkdev92.StackChan.Gateway.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Register your own implementations before these calls. Both use TryAdd, so whatever is
// already registered wins and the SDK never needs editing.
builder.Services.AddSingleton<ISpeechToText, MyRecognizer>();
builder.Services.AddSingleton<ITextToSpeech, MySynthesizer>();
builder.Services.AddSingleton<IAgent, MyAgent>();

builder.Services.AddStackChanRuntime(builder.Configuration);
builder.Services.AddStackChanAtoms3R(builder.Configuration);

var app = builder.Build();
app.MapStackChanAtoms3RConverse();   // POST /v1/converse
app.Run();
```

---

## Why Kkdev92.StackChan.Gateway

The firmware defines a fixed byte-level protocol, while applications need to choose their own
recognizer, model, capabilities, synthesis service, and failure policy. Keeping those concerns
separate lets an application replace providers without changing the device protocol.

The wire contract therefore lives in one package, while the orchestration lives in another that
does not depend on HTTP or SSE. Raw-byte conformance checks are available separately. Architecture
tests enforce the package boundaries, and the reference host consumes packed `.nupkg` files so
that package-consumption problems are caught before release.

- The SDK implements the device protocol directly and does not expose an `IDeviceProtocol`
  abstraction
- The reference host uses only the public SDK packages and has no private integration surface
- Named, tested adapters contain compatibility handling for specific local-model APIs
- Documentation records both values when the firmware limit and the gateway's accepted limit
  differ

---

## Usage

### Add a capability

A capability is a plain class. It never names an Agent Framework type, so it can be called
directly from a test.

```csharp
using Kkdev92.StackChan.Gateway.Abstractions;

public sealed class DiceCapability : ICapability
{
    [CapabilityAction("roll_dice", "Rolls a die.", Triggers = ["dice", "die"], IsReadOnly = true)]
    public string Roll() => Random.Shared.Next(1, 7).ToString();
}
```

`Triggers` exists because small local models decline to pick a tool under `tool_choice: auto`.
When an utterance contains a trigger word the capability runs first and the result is handed to
the model. It only applies when `IsReadOnly` is `true` — otherwise saying "turn the lights off"
would turn them off without the model ever deciding to.

### Replace a provider

```csharp
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Audio;

public sealed class MySynthesizer(HttpClient http) : ITextToSpeech
{
    public async Task<PcmAudio> SynthesizeAsync(string text, CancellationToken ct)
    {
        var wav = await http.GetByteArrayAsync(BuildUri(text), ct);

        // The shared conversion keeps the sample format consistent with the device
        // contract and avoids duplicating WAV parsing and resampling code.
        return new PcmAudio(
            WavAudio.ToTargetPcm(wav),
            PcmAudio.CanonicalSampleRate,
            PcmAudio.CanonicalChannels);
    }
}
```

### Verify your host against the contract

```csharp
var violations = ConformanceChecks.Run(
    response.Content.Headers.ContentType?.ToString(),
    await response.Content.ReadAsByteArrayAsync(),
    expectedUtf8Texts: ["hello"]);

violations.ShouldBeEmpty();
```

### Drive the wire without a device

```bash
dotnet run --project tools/device-sim -- --scenario all
```

The `all` scenario covers repeated turns, concurrent turns, a mid-stream disconnect, an over-long
utterance, and malformed requests. Each check has an expected status or protocol result.

Configuration is in [`docs/configuration.md`](docs/configuration.md); running and observing a
live gateway is in [`docs/operations.md`](docs/operations.md).

---

## Properties Checked in CI

- **Package boundaries.** Forbidden reference directions are asserted, including
  `HttpContext` reaching `Runtime` and `Runtime` seeing which agent framework is in use
- **`Abstractions` has no package, project, or explicit framework dependencies.** Zero
  `PackageReference`, zero `ProjectReference`, zero `FrameworkReference`, checked in CI
- **A package that does not work as a package fails before release.** The reference host
  restores the SDK from a local feed, so its tests run against packed `.nupkg` files rather than
  project references
- **The conformance checks can detect violations.** Mutation tests feed malformed streams to the
  13 checks and require the corresponding violations to be reported
- **A capability failure does not end the turn.** `CapabilityCall.AnswerAsync` returns words on
  failure and lets only cancellation through
- **No device or conversation identifiers in metrics.** The instruments carry counts, durations,
  outcomes, provider names, and capability names without device or conversation labels
- **One version across all eight packages.** Releasing one alone would leave the others
  pointing at an older contract, so the tag, `VersionPrefix` and the eight central entries are
  checked against each other

---

## Known Limitations

- **One device family.** The scope is AtomS3R stack-chan firmware. The SDK implements that wire
  format directly and does not expose a device-protocol abstraction
- **Plain HTTP between device and gateway.** The firmware does not implement TLS, so the audio,
  the transcript and the token are visible to anything watching the local network. This is a
  property of the device, not a setting here — see [SECURITY.md](SECURITY.md)
- **Provider latency is deployment-specific.** Model inference and speech synthesis usually
  dominate time to first audio. Sentence-at-a-time synthesis streams completed sentences but
  does not make the configured providers faster
- **`net10.0` only.** The packages do not multi-target other frameworks
- **No retry.** A circuit breaker refuses calls to a service that keeps failing, but nothing is
  resent: re-posting audio would send it twice, and the firmware decides whether to retry
- **The reference host is configured for Japanese.** The SDK itself does not impose a language;
  other languages require appropriate prompts, recognition, and synthesis providers
- **The reference host is not published.** It is in the repository to be read and forked, not
  installed; there is no `dotnet tool` and no container image
- **No management API.** The gateway does not call back into the device. Device-side settings go
  through the firmware's own HTTP API

---

## How It Works

```text
device: 3 s of 16 kHz audio      POST /v1/converse, or JSON text instead
        v
Protocol.Atoms3R                 headers, token, size — refused here, before any SSE
        v
Runtime                          one turn, with a deadline and one turn per session
        v
ISpeechToText                    audio to text
        v
IAgent                           text in, sentences out, streaming
        v
ITextToSpeech                    one sentence ahead of playback
        v
Protocol.Atoms3R                 SSE envelopes, 4,096 bytes of PCM each, seq from 0
        v
device: plays as it arrives      conversation.finished, or an error event
```

The runtime never sees an SSE frame; it reports that audio is available. Turning that into
`data: {...}` lines inside the device's byte caps belongs to `Protocol.Atoms3R`, and that
separation lets providers and application behavior change without altering the wire contract.

Full detail is in [`docs/architecture.md`](docs/architecture.md).

---

## Platform Requirements

|  |  |
| --- | --- |
| .NET | `net10.0` — single target, no multi-targeting |
| Language | C# 14; `LangVersion` is never `latest` or `preview` |
| SDK (to build) | `10.0.303`, pinned in `global.json` with `rollForward: latestPatch` |
| Runtime dependencies | none in `Abstractions`; `Microsoft.Extensions.*` abstractions elsewhere; Microsoft Agent Framework and the OpenAI client in `AgentFramework` only |
| Scripts | PowerShell 7 (`pwsh`), cross-platform |
| Device | AtomS3R with M5Stack Atomic Voice Base, running [stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r) `v0.1.0` or later |
| Downstream services | speech-to-text, an OpenAI-compatible model endpoint, and text-to-speech. None are bundled |
| CI host OS | Windows and Ubuntu |

The device firmware version and this package version are **independent axes**. A firmware
release does not by itself cause a major bump here; only a breaking change to *this* public API
does.

---

## Security and Privacy

- **No Outbound Telemetry**: metrics and traces remain in process unless the host configures an
  exporter. The gateway opens connections only to services configured by the operator
- **A Token Is Required**: unless fixed-response mode is on, startup fails without
  `StackChan:Atoms3R:Token`; for values of equal byte length, comparison time does not depend on
  which bytes match
- **Tokenless LAN Listeners Are Rejected by Default**: if an unauthenticated gateway would listen
  on a non-loopback address, startup stops. `StackChan:Security:AllowUnauthenticatedLan=true` is
  the only way through
- **Conversation Content Stays Out of Logs**: transcripts and replies are logged by length, not
  by value; query strings are stripped from provider logs; the startup configuration dump prints
  the length of a secret rather than the secret
- **Tracked Defaults Contain No Secrets**: `appsettings.json` ships with empty secret values;
  provide tokens and API keys through environment variables
- **Untrusted Input Is Bounded**: request bodies, identifier length and character set, utterance
  length and downstream response size all have caps, and a downstream response is read under a
  byte limit rather than buffered freely
- **The Gateway Does Not Persist Audio**: the repository writes a recording to disk only when
  `device-sim` is run with `--save`; downstream services may have their own retention policies

**Transport is not encrypted.** The device firmware does not implement TLS. Use a network you
control, and do not expose the gateway to the internet. [SECURITY.md](SECURITY.md) has the threat
model and the reporting process.

---

## Documentation

|  |  |
| --- | --- |
| [Architecture](docs/architecture.md) | Design intent, package boundaries, and enforced requirements |
| [Configuration](docs/configuration.md) | Every setting, its default, and why the default is what it is |
| [Operations](docs/operations.md) | Running it, health endpoints, metrics, and narrowing down a failure |

The index, with a reading order for each kind of reader, is in [`docs/`](docs/README.md).

The device wire contract is owned by the firmware repository, not this one:
[stackchan-atoms3r/docs/api/device-interface.md](https://github.com/kkdev92/stackchan-atoms3r/blob/main/docs/api/device-interface.md).

---

## Contributing

Bug reports, documentation fixes, and focused pull requests are welcome. Thank you for taking
the time to improve the project.

```bash
pwsh eng/build-all.ps1
```

That command runs the SDK tests, packs the SDK, and then runs the reference host tests against
the packed packages. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow,
enforced design requirements, and line-ending configuration.

Helpful things when reporting bugs:

- The package versions, or the commit if you built from source, and `dotnet --version`
- Whether it also happens in fixed-response mode
  (`start-gateway.ps1 -Offline -Token <32-char-token>`), which removes the recognizer, model, and
  synthesizer from the request path
- Whether `device-sim --scenario all` passes
- The startup configuration log, after redacting endpoints, model names, and other deployment details
- The firmware version, from the device's `device.describe`

Please do not include a device token, an API key, or a recording in an issue. Issues are public;
redact logs and reproduction details before posting them.

---

## Support & Maintenance Policy

This is a personal hobby project maintained in spare time. It is active, but support is
best-effort: I'll do my best to review issues and PRs, and releases may be a bit slow sometimes —
thank you for your patience.

The `0.x` line is pre-release. Breaking changes are expected before `1.0.0` and are listed in the
[CHANGELOG](CHANGELOG.md). From `1.0.0` onward the public API follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). All eight packages move together.

Really appreciate you using it 💛

---

## License

[MIT](LICENSE). Third-party attributions are in [NOTICE](NOTICE), which ships inside every
package — keep it with anything you redistribute.

---

## Acknowledgments

- Stack-chan is a project by [Shinya Ishikawa](https://github.com/meganetaaan/stack-chan),
  licensed Apache-2.0. This is a third-party community project, not affiliated with, endorsed by
  or sponsored by the Stack-chan project
- The device it talks to is [stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r),
  which owns the wire contract this implements
- Built on [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) and
  [Microsoft.Extensions.AI](https://github.com/dotnet/extensions)
- Exercised against [whisper.cpp](https://github.com/ggml-org/whisper.cpp),
  [Foundry Local](https://learn.microsoft.com/azure/ai-foundry/foundry-local/) and
  [VOICEVOX](https://voicevox.hiroshiba.jp/). None are bundled, and each carries its own terms —
  VOICEVOX in particular has terms on the use of its voices
- Tested with [xUnit](https://xunit.net/) and [Shouldly](https://github.com/shouldly/shouldly)
