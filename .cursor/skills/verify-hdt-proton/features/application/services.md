# Use account and streaming services

Users connect account-backed advice, collection and replay services, and expose
tracking information to streaming tools.

## Sub-features

- `service-account`: sign in, sign out, and display account state.
- `service-collection`: synchronize collection information with explicit preferences.
- `service-replays`: configure uploads, inspect latest replays, and open a replay.
- `service-advice`: distinguish available, unavailable, and expired account advice.
- `service-streaming`: configure capturable overlay and Twitch extension output.

## How to get to it (user POV)

Use **HSReplay.net**, the account, collection, and replay Settings pages, or
**Settings > Streaming**. Replay entry points also appear in the main menu and statistics.

## Driving it with original reference capture and authorized services

Preconditions: the real service, authorized test account, required entitlement,
and an explicitly owned destination. Keep tokens, account identifiers, and raw
account screenshots out of public evidence.

- Reference capture can inventory the original controls and their empty states.
  It cannot establish authentication, upload, or entitlement behavior.
- A service journey must observe sign-in and sign-out effects, a known collection
  or replay operation, the actual destination, and expired or denied access.
  No executable native account journey currently exists.
- Supplied native ratings can test display and clearing through the
  [observation bridge](../native-avalonia.md#optional-native-observation-bridge).
  Record that scope explicitly. It does not pass authenticated advice acquisition.
- Capturable-overlay and Twitch output need destination-specific journeys.
  Current native X11 pixel tests do not cover them.

## Gotchas

The native HSReplay menu is disabled. The client makes no account requests.
Entitlement in original HDT does not establish native service compatibility or
permission to redistribute a provider's assets and data.
