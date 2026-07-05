# Issue tracker

## Where issues live

This repo does not use a formal issue tracker (GitHub Issues, Jira, Linear, etc.).

Issues, feature requests, and tasks are captured and tracked through natural-language prompts and conversation in the agent chat. The user describes the work, and the agent records context, decisions, and progress in the chat and in the repo's documentation.

## Creating an issue

Do not call `gh issue create` or any external issue-tracker API.

When the user reports a bug or asks for a feature, treat the chat message itself as the issue. Summarize the request, ask clarifying questions if needed, and proceed to triage or implementation directly.

## PRs as a request surface

Not applicable — there is no external issue tracker.
