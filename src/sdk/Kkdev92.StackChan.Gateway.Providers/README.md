# Kkdev92.StackChan.Gateway.Providers

Shared parts for writing a provider.

When you implement speech-to-text or text-to-speech, the parts that end up the
same in every implementation are here. You write *what to call*; this package
handles *how to convert*.

| Type | Role |
| --- | --- |
| `WavAudio` | WAV to normalized samples (parse, mix down, resample to 16 kHz) |
| `WavWriter` | Normalized samples to a WAV with a 44-byte header |
| `NonSpeechAnnotations` | Strips annotations a recognizer adds, such as `(music)` |
| `ProviderEndpoint` | Endpoint validation, and how a failure is classified |
| `ProviderResponse` | Reads a response body under a byte cap instead of buffering it whole |
| `ProviderCircuitBreaker` | Stops calling a downstream service that keeps failing |

Using the shared audio conversion keeps the wire format consistent with the device contract and
avoids duplicating WAV parsing and resampling code.

## Where this package sits

StackChan Gateway SDK provides building blocks for a gateway that converses
with an AtomS3R stack-chan. Applications select the speech recognizer, model,
capabilities, and other behavior.

| Package | Role |
| --- | --- |
| `Kkdev92.StackChan.Gateway.Abstractions` | The contracts. No NuGet package dependencies |
| `Kkdev92.StackChan.Gateway.Runtime` | Turn orchestration without HTTP or SSE dependencies |
| `Kkdev92.StackChan.Gateway.Protocol.Atoms3R` | The wire contract with the device |
| `Kkdev92.StackChan.Gateway.AgentFramework` | An `IAgent` backed by Microsoft Agent Framework |
| `Kkdev92.StackChan.Gateway.Providers` | Shared parts for writing a provider |
| `Kkdev92.StackChan.Gateway.Capabilities` | Shared parts for writing a capability |
| `Kkdev92.StackChan.Gateway.Diagnostics` | Fixed components for testing a device connection without external services |
| `Kkdev92.StackChan.Gateway.TestKit` | Wire-protocol conformance checks and hand-written test fakes |

- Repository: <https://github.com/kkdev92/stackchan-atoms3r-gateway>
- License: MIT
- Targets: .NET 10

> **Note:** this SDK is under development (`0.1.x`). The public surface may change
> before 1.0.
