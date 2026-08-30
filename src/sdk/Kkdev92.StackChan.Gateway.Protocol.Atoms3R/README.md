# Kkdev92.StackChan.Gateway.Protocol.Atoms3R

The wire contract with an AtomS3R stack-chan.

Implements the SSE envelope, base64 PCM, `seq` numbering, and header validation.
**Device-specific concerns stop here** and stay invisible to the runtime - an
anti-corruption layer.

```csharp
services.AddStackChanAtoms3R(configuration);
// ...
app.MapStackChanAtoms3RConverse();   // POST /v1/converse
```

The device firmware defines the wire format, and hosts are expected to conform to that contract.
Proposed protocol changes should be discussed in the
[firmware repository](https://github.com/kkdev92/stackchan-atoms3r). Use the 13 checks in
`Kkdev92.StackChan.Gateway.TestKit` to verify that your own host conforms.

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
