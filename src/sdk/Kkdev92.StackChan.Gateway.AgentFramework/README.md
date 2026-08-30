# Kkdev92.StackChan.Gateway.AgentFramework

An `IAgent` backed by Microsoft Agent Framework.

Connects to an OpenAI-compatible endpoint (Foundry Local, LM Studio, Ollama, and
similar) and hands your capabilities over as tools. **Framework types never leave
this package.**

It also includes compatibility layers for behaviors commonly seen with small local models.

- Tool calls that come back as body text are parsed into structured calls
- When an utterance hits a trigger word, the capability runs ahead of time and
  the result is handed to the model - small models often will not pick a tool
  under `tool_choice: auto`
- Optional parameters whose type arrives as an array are collapsed, because some
  endpoints reject a type array

```csharp
services.AddStackChanAgentFramework(configuration);
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
