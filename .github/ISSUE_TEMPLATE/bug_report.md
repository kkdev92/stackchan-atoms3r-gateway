---
name: Bug report
about: Something does not behave as documented
title: '[Bug] '
labels: bug
assignees: ''
---

Thank you for taking the time to report a problem.

If the report concerns a security vulnerability, please use
[private vulnerability reporting](https://github.com/kkdev92/stackchan-atoms3r-gateway/security/advisories/new)
instead of this public template.

## What happened?

## Steps to reproduce

1.
2.
3.

## What did you expect to happen?

## Narrowing it down

Fixed-response mode removes downstream services from the request path and helps identify whether
to investigate the device connection, the gateway, or a configured provider. If available,
please try:

```bash
pwsh eng/start-gateway.ps1 -Offline -Token <32-char-token>
```

These checks are optional; leave any unavailable item unchecked.

- [ ] The problem also occurs in fixed-response mode
- [ ] The problem occurs only with configured providers
- [ ] `dotnet run --project tools/device-sim -- --scenario all` passes
- [ ] The problem occurs only on physical hardware

## Environment

- Package versions, or the commit if built from source:
- .NET SDK (`dotnet --version`):
- OS and architecture:
- Firmware version (from the device's `device.describe`):
- Downstream services in use (recognizer, model, synthesizer):

## Logs

The startup configuration log prints secret lengths rather than values, but it
also contains endpoints and model names. Remove tokens, API keys, private
addresses, deployment details, and recordings before posting it.

```text

```

## Additional context
