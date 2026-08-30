# Kkdev92.StackChan.Gateway.Abstractions

Core contracts for the gateway.

Defines the types for audio, turns, conversations, and capabilities. It has no NuGet package
dependencies and no project or explicit framework references. Architecture tests enforce this
boundary.

Every other package builds on this one. If you write your own capability or
provider, this is the only package you need to reference.

```csharp
using Kkdev92.StackChan.Gateway.Abstractions;

// A capability is an ordinary .NET class. It knows nothing about
// Agent Framework or HTTP.
public sealed class DiceCapability : ICapability
{
    [CapabilityAction(
        "roll_dice",
        "Rolls a die.",
        Triggers = ["dice", "die"],
        IsReadOnly = true)]
    public string Roll() => Random.Shared.Next(1, 7).ToString();
}
```

There are six interfaces, and all six are extension points: `ISpeechToText`,
`ITextToSpeech`, `IAgent`, `ITurnRuntime`, `ISessionRegistry`, and `ICapability`.

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
