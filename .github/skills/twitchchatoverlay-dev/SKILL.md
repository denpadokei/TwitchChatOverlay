---
name: twitchchatoverlay-dev
description: 'Development workflow for TwitchChatOverlay (WPF/.NET). Use when you need local build setup, local.props secrets wiring, YouTube proto-service consistency checks, and release document verification.'
argument-hint: 'Task focus: build | local-secrets | youtube-proto | docs-check'
user-invocable: true
---

# TwitchChatOverlay Development Workflow

## What This Skill Does
This skill provides a repeatable workflow for common development tasks in the TwitchChatOverlay repository:
- Local build readiness checks
- Local secrets setup via local.props template
- YouTube gRPC proto and service consistency review
- Release/legal document consistency checks

## When to Use
Use this skill when the request includes keywords like:
- build, compile, dotnet build, WPF
- local.props, client id, secrets, OAuth config
- YouTube proto, gRPC, live chat stream
- privacy policy, terms, docs sync, release docs

## Prerequisites
- Windows environment with .NET SDK installed
- Repository root is available
- You should never commit real secrets to source control

## Procedure
1. Validate repository context.
   - Confirm main project files are present:
     - `README.md`
     - `TwitchChatOverlay/TwitchChatOverlay.csproj`
     - `build/local.props.example`

2. Prepare local secrets configuration.
   - Copy `build/local.props.example` to `build/local.props` if missing.
   - Fill `TwitchClientId`, `YouTubeClientId`, and `YouTubeClientSecret` for local debugging only.
   - Keep `build/local.props` uncommitted.

3. Run baseline build.
   - Execute `dotnet build TwitchChatOverlay/TwitchChatOverlay.csproj`.
   - If build fails, capture compile errors and fix only relevant files.

4. Check YouTube proto-service consistency.
   - Review `TwitchChatOverlay/Protos/youtube_live_chat_stream.proto`.
   - Verify usage alignment in `TwitchChatOverlay/Services/YouTubeLiveChatService.cs`.
   - Ensure request/response field expectations match current stream handling logic.

5. Verify release and compliance documents.
   - Review repository-level docs in `docs/` and app-embedded docs in `TwitchChatOverlay/Docs/`.
   - Confirm privacy/terms documents are aligned between distribution and site copies.

6. Summarize outcomes.
   - Report changed files, validation performed, unresolved issues, and suggested next steps.

## Repository References
- `./../../README.md`
- `./../../build/local.props.example`
- `./../../TwitchChatOverlay/TwitchChatOverlay.csproj`
- `./../../TwitchChatOverlay/Protos/youtube_live_chat_stream.proto`
- `./../../TwitchChatOverlay/Services/YouTubeLiveChatService.cs`
- `./../../TwitchChatOverlay/Docs/PrivacyPolicy.html`
- `./../../TwitchChatOverlay/Docs/TermsOfUse.html`
