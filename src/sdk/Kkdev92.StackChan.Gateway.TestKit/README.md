# Kkdev92.StackChan.Gateway.TestKit

Conformance checks against the wire contract, and hand-written fakes.

Verifies that your own host speaks the device contract, against the **raw bytes**.

- 13 conformance checks (envelope shape, `seq` running in order, the per-event
  byte cap, no Unicode escapes on the wire, and more)
- SSE reassembly (`SseWire`)
- Requests shaped like the device sends them (`DeviceRequest`, `WavFactory`)
- Hand-written fakes that satisfy the contracts (`Fakes`)

```csharp
var violations = ConformanceChecks.Run(
    response.Content.Headers.ContentType?.ToString(),
    await response.Content.ReadAsByteArrayAsync(),
    expectedUtf8Texts: ["hello"]);

violations.ShouldBeEmpty();
```

**Its only dependency is `Abstractions`** - it does not depend on a test framework.

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
