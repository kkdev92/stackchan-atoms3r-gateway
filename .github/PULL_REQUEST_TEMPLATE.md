## Summary

Describe what changed and why.

## Related issue

Fixes #

## Verification

Check the items that apply. It is fine to leave an item unchecked and explain why
it was not available in your environment.

- [ ] `pwsh eng/build-all.ps1`
- [ ] `dotnet format --verify-no-changes`
- [ ] `dotnet restore --locked-mode`
- [ ] `dotnet run --project tools/device-sim -- --scenario all`
- [ ] Physical hardware check; firmware version:

Paste only the relevant output, after removing secrets and private deployment details.

## Interface and documentation

- [ ] A change in behavior has a test that fails without it
- [ ] Public SDK API has XML doc comments
- [ ] A package's README is updated if its surface changed
- [ ] `docs/architecture.md` is updated if the layout or a design rule changed
- [ ] `CHANGELOG.md` has an entry under Unreleased, if user-visible
- [ ] No token, API key, private address, or recording is included

## Notes for reviewers

List remaining risks, checks, or follow-up work. If an existing assertion was
changed, explain the reason for the change.
