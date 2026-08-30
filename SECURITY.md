# Security Policy

## Supported versions

Only `main` and the most recent `0.1.x` packages are supported. Fixes land on `main` and are
included in the next release; older versions are not maintained as separate supported lines.
All eight packages move together.

## Reporting a vulnerability

Please use
[GitHub Private Vulnerability Reporting](https://github.com/kkdev92/stackchan-atoms3r-gateway/security/advisories/new)
to report security issues rather than opening a public issue.

If that form is unavailable, contact <kkdev92.dev@gmail.com> with `security` in the subject.

Do **not** include the device token, an API key, Wi-Fi credentials, a private address, or a
recording in a report. The startup configuration log replaces secret values with their lengths,
but still contains endpoints and model names. Review and redact it before attaching it.

This project is maintained on a best-effort basis. You can expect an acknowledgment and an
initial assessment when the report has been reviewed. Remediation timing depends on the impact
and the availability of a safe fix.

## Scope

This project covers the gateway side of a conversation with a device. Vulnerabilities in the
device firmware should usually be reported to
[stackchan-atoms3r](https://github.com/kkdev92/stackchan-atoms3r), and vulnerabilities in a
downstream service should usually be reported to that service. If you are unsure which project
owns a problem, report it privately here and we will help identify the appropriate destination.

In scope:

- A way to reach `POST /v1/converse` without the token, or a timing signal that narrows it
- A request shape that gets past the identifier, size or utterance checks
- A path that writes a token, an API key or conversation content to a log or a response
- Unbounded memory, CPU or file growth driven by input an attacker controls — including a
  response from a downstream service, which is untrusted input here
- A dependency advisory that `NuGetAudit` does not already surface

Out of scope:

- The gateway listening on `0.0.0.0`. This is the documented default; normal provider mode
  requires a token, and tokenless fixed-response mode requires an explicit LAN override
- Plain HTTP between device and gateway. See below; it is a firmware constraint
- Anything requiring physical access to the device or the host
- Fixed-response mode being unauthenticated on loopback. Startup rejects the equivalent LAN
  configuration unless it is explicitly allowed

## Security posture

The gateway is meant to run on a home network beside the device it serves. The threat it is built
against is another client on the same network — eavesdropping, or starting a conversation without
permission. It is not built against exposure to the internet, and should not be arranged that
way.

**A token is required.** Unless fixed-response mode is on, startup fails without
`StackChan:Atoms3R:Token`. The comparison uses a fixed-time primitive. For values with equal byte
lengths, comparison time does not depend on which bytes match; request length remains observable.

**Tokenless LAN exposure is blocked by default.** If a tokenless gateway would listen on a
non-loopback address, startup stops rather than continuing.
`StackChan:Security:AllowUnauthenticatedLan=true` is the only way through, and setting it is a
statement of intent. The listen address is not quietly moved to loopback; the configured address
remains visible and predictable.

**Sensitive content is excluded from the gateway's structured logs.** Tokens and API keys are
represented by their length, transcripts and replies are logged by length, and the weather HTTP
logger removes query strings. Review logs before sharing them because endpoints, model names,
exception types, and other deployment details remain visible.

**Untrusted input is bounded** on both sides. From the device: request body (2 MiB by default),
identifier length and character set, and the utterance cap. From a downstream service: every
response is read under a byte limit rather than buffered freely, a tool call parsed out of body
text is length-capped and count-capped, and a WAV with a malformed chunk length is rejected instead
of looped over.

**Capability and synthesis failures have fallbacks.** `CapabilityCall.AnswerAsync` returns a
configured spoken message after a capability failure, and a synthesis failure leaves the sentence
available as text. Other provider failures are reported through protocol error events.

**The gateway does not persist audio.** Code in this repository writes a recording to disk only
when `device-sim --save` is used explicitly. Downstream services may retain audio under their own
policies. `.gitignore` excludes `*.wav`, `*.mp3` and `*.pcm` from normal Git additions.

## Transport is not encrypted — action required

The device firmware does not implement TLS, so `POST /v1/converse` is plain HTTP. The audio, the
transcript, the reply and the token are all visible to anything that can observe the local
network. The token travels in a header on every request.

This is a property of the device, not a setting on this side, and no configuration here changes
it. Use a network you control, do not expose the gateway to the internet, and treat the token as
something that a network observer already has.

## Secrets and where they live

|  |  |
| --- | --- |
| Device token | `StackChan__Atoms3R__Token`, or `start-gateway.ps1 -Token` |
| Weather API key | `StackChan__Weather__ApiKey` |
| Model endpoint key | `StackChan__Agent__ApiKey`. A placeholder for a local endpoint |

`appsettings.json` ships with empty strings where secrets go. Do not fill them in; it is a tracked
file, so Git ignore rules cannot protect secrets added to it.

The weather service receives its API key in the query string. The gateway replaces the standard
HTTP logger for that client with one that always removes the query, even when
`System.Net.Http.DisableUriRedaction=true`.

## Not a compliance guarantee

This software records audio around the device and sends it to services selected by the operator.
Operators are responsible for evaluating consent, retention, downstream handling of recordings,
and terms that apply to synthesized voices in their deployment. This gateway does not by itself
provide compliance or licensing assurances.
