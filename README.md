# Reply Orbs

Native Windows WPF application for floating reply snippets, persistent file attachments and an optional DeepSeek assistant. Imported from independently checked version 0.1.4.

## Build and verify

Windows x64 with .NET Framework 4.8 is required. The compiler and framework assemblies come with Windows; no package restore is needed.

```powershell
./ci/check.ps1
./build.ps1 -Test
```

The build treats compiler warnings as errors. The test suite runs at least 157 checks covering storage, attachments, DPAPI, clipboard payloads, async generation, paste consent and WPF state transitions. Dock assertions check a smooth fade when Windows enables animation, or immediate transitions without clocks when it disables animation. A separate fixture also verifies reduced motion. It uses synthetic profiles and HTTP handlers, without a real API key or external API calls. Output is `ReplyOrbs.exe`; test results are in `verification.txt`. Both are ignored by Git.

## CI and main

Every PR targeting `main` runs two checks: **Repository checks** and **Windows build and tests**. Actions are pinned to immutable commit SHAs, have read-only repository permissions and do not persist checkout credentials. Failed or timed-out tests fail CI; the test report is retained as an artifact.

The public repository's active GitHub protection is recorded in `.github/main-protection.json`: PR required, both checks required from the GitHub Actions app, branch up to date, all conversations resolved, linear history, no force push or deletion, and no admin bypass. Human approvals are not mandatory; a PR and green CI are mandatory.

To reapply the policy:

```powershell
gh api --method PUT repos/pinnnthreetriples/reply-orbs/branches/main/protection --input .github/main-protection.json
```

Use a feature branch and a PR for changes.

## Local data and validation limits

Normal runtime data is stored in `%LOCALAPPDATA%/ReplyOrbs`, with `Files`, `Notes`, a reply manifest and a DPAPI-protected key. Local builds, old deliverables, QA profiles, personal replies and credentials are excluded from this repository.

CI verifies actual WPF objects and intermediate states, not every frame of the Windows compositor. A live DeepSeek request, compatibility in WhatsApp/MAX and multiple monitors with different DPI require separate validation. The application never sends chat messages automatically.
Reply Orbs: Windows floating reply snippets and DeepSeek assistant
