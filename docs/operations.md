# Operations

Running a gateway, watching it, and narrowing down a failure. Settings themselves are in
[`configuration.md`](configuration.md).

## Starting it

```bash
# Fixed responses over the LAN. The token must match the device configuration.
pwsh eng/start-gateway.ps1 -Offline -Token <32-char-token>

# Real services. A token is mandatory.
pwsh eng/start-gateway.ps1 -Token <32-char-token>
```

Bring a device up in fixed-response mode first. This checks the device-to-gateway connection
without starting the recognizer, model, or synthesizer.

Startup refuses to continue in two cases, and says which:

- A token is required (not fixed-response mode) and none is set
- No token *and* a non-loopback listen address. `StackChan:Security:AllowUnauthenticatedLan=true`
  is the only way through

## The startup dump

The gateway writes the effective runtime limits and the sections used by the selected mode at
`Information`. Secret values are replaced by their length. Endpoints and model names remain in
the output, so review and redact it before posting it publicly:

```text
config section=StackChan:Atoms3R  Token=set(32) MaxRequestBodyBytes=2097152 …
config section=StackChan:Agent    Endpoint=http://127.0.0.1:5273/v1 Model=Phi-4-mini-… …
```

The firmware requires a 32-character token. A value such as `set(31)` identifies a length error
without exposing the token itself.

There is deliberately no HTTP endpoint for this. Exposing the configuration over the network would
publish the shape of the deployment to anyone on the LAN.

## Health

Three endpoints, split because they answer different questions and cost different amounts.

|  |  |
| --- | --- |
| `GET /health` | Liveness. Does not call downstream services and is intended for restart decisions |
| `GET /health/ready` | Readiness. Returns 503 once the host begins shutting down — that is the only difference from `/health`, and it is why `/health` cannot be used to decide when to stop sending |
| `GET /health/providers` | Deep check. Whether each downstream service is listening. Costs outbound connections, so the result is cached for 2 seconds |

`/health/providers` checks only that something is listening. It does not run inference, and it
uses TCP connection checks because the configured providers do not share a common HTTP health
endpoint. A listening service may still fail an actual request, which is why the field is named
`listening`. Endpoints are omitted because they describe the deployment.

The cache is what bounds the cost. `/health/providers` needs no token, so without it one
unauthenticated endpoint could be used to make the gateway call outward repeatedly.

## Metrics

The SDK defines six instruments on a BCL `Meter` named `StackChan.Gateway`, plus one
`ActivitySource`. It has no OpenTelemetry package dependency, and `dotnet-counters` can read the
`Meter` directly.

```bash
dotnet-counters monitor --process-id <pid> --counters StackChan.Gateway
```

|  |  |
| --- | --- |
| `stackchan.turn.duration` | One turn, in ms. Labelled by outcome |
| `stackchan.turn.first_audio` | Turn execution start to first audio, in ms. Use it to track gateway response latency |
| `stackchan.turns.active` | Turns running now |
| `stackchan.provider.duration` | One downstream call, in ms. Labelled by provider and outcome |
| `stackchan.provider.breaker.opened` | Circuit breaker openings |
| `stackchan.capability.calls` | Capability executions. Labelled by name and outcome |

No device identifier is used as a label. Per-device labels create an unbounded label set; use the
structured log when a device-specific diagnosis is required.

To wire it into OpenTelemetry, add the names on the host side:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("StackChan.Gateway"))
    .WithTracing(t => t.AddSource("StackChan.Gateway"));
```

**Where the time goes.** Model inference and speech synthesis usually dominate the delay before
the first audio event. The gateway synthesizes and streams one sentence at a time so the device
does not have to wait for the complete response. Use `provider.duration` to identify the slow
provider in your own deployment.

Absolute latency depends heavily on the selected model, hardware, and whether the model is warm.
For that reason, the automated performance tests isolate the gateway with fakes and verify how
its cost grows with input instead of enforcing a provider-specific wall-clock threshold.

## Deployment

The gateway, speech recognizer, model server, and synthesizer can run on one machine, but the
model and speech services determine most of the memory requirement. Measure the complete stack
with the models you intend to use and leave headroom for model warm-up and concurrent turns.
If memory is tight, select a smaller model or move one or more providers to another host.

The providers are plain HTTP services and can run in WSL, in a container, or on another machine
on the LAN. A provider that binds only to loopback must remain on the gateway host unless you add
a relay or change that provider's listen address.

## Narrowing down a failure

Work down this list. Each step removes something from the picture.

| Symptom | Next step |
| --- | --- |
| The robot says nothing at all | Try fixed-response mode. If it speaks, investigate the configured downstream providers |
| Fixed-response mode is also silent | Run `device-sim --scenario single`. If that passes, check the device token, gateway URL, and network path between the device and gateway |
| The robot speaks unexpected words | Inspect the recognizer output. Logs carry its length, not its text, so run the recognizer directly with the same audio |
| It worked and then stopped | `/health/providers`, then `stackchan.provider.breaker.opened`. A breaker that opened is refusing calls for its cooldown |
| Long silence before it speaks | Compare `stackchan.turn.first_audio` with `stackchan.provider.duration[model]`. If they track each other, model latency is likely the largest component |
| A request is refused and it is unclear why | Check the startup configuration log for effective limits and environment-variable overrides |
| Refusals with no explanation | `device-sim --scenario reject` reproduces all eight, each with its expected status and message |

`device-sim` needs a running gateway:

```powershell
$env:STACKCHAN_DEVICE_TOKEN = '<same-32-char-token>'
dotnet run --project tools/device-sim -- --scenario all
dotnet run --project tools/device-sim -- --text hello --save reply.wav
```

`--text` skips recognition and sends the utterance directly, allowing the model and synthesizer
to be checked without a microphone. Use text supported by the configured model and voice.

`--save` writes the returned audio to disk. The gateway itself does not persist recordings.

## What the simulator cannot tell you

The simulator can send malformed requests that the firmware does not produce. It cannot verify
the microphone, speaker, face, emergency stop, or end-to-end latency on real hardware. The two
test paths cover different behavior; hardware testing is described in
[`architecture.md`](architecture.md#tests).
