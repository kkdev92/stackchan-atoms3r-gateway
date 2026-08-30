# Kkdev92.StackChan.Gateway.Capabilities

Shared parts for writing a capability.

When you implement a capability, the parts that end up the same in every one are
here.

| Type | Role |
| --- | --- |
| `CapabilityCall.AnswerAsync` | Runs with a deadline and **returns words even on failure** |
| `SpokenText.Number` | Numbers formatted for speech, independent of culture |
| `CapabilityEndpoint` | Endpoint validation |

`CapabilityCall.AnswerAsync` converts capability failures into a spoken response so the
conversation can continue. Cancellation is passed through because it indicates that the turn
has ended.

```csharp
return await CapabilityCall.AnswerAsync(
    async token => await CallSomethingAsync(token),
    whenUnavailable: "I could not look that up.",
    timeout: TimeSpan.FromSeconds(10),
    cancellationToken);
```

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
