# Kkdev92.StackChan.Gateway.Runtime

Turn orchestration.

Assembles one exchange - listen, answer, shape the text, speak - into a single
turn. It **knows nothing about HTTP or SSE**: the shape that goes on the wire
belongs to `Kkdev92.StackChan.Gateway.Protocol.Atoms3R`.

What is here: turn execution and deadlines, session tracking and serialization,
an admission gate for concurrent turns, sentence splitting, expression markers,
and text shaping for speech.

```csharp
services.AddStackChanRuntime(configuration);
```

`ITurnRuntime` and `ISessionRegistry` are registered with `TryAddSingleton`, so
registering your own before this call replaces them.

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
