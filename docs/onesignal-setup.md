# OneSignal setup

## How the integration works

- Devices register their OneSignal **subscription id** with the API:
  `POST /api/v1/devices { oneSignalSubscriptionId, platform }`. Re-registering
  a subscription under a different account transfers ownership (audited) —
  stale handovers are handled.
- All notifications are written to `notification_history` (in-app inbox) and
  `notification_outbox` **inside the business transaction** (ADR-0006). The
  Worker delivers the outbox to OneSignal (`POST
  https://api.onesignal.com/notifications` with
  `include_subscription_ids`), records the provider message id, retries
  transient failures with bounded jittered backoff (max 8 attempts), and
  recovers leases abandoned by a crashed worker.
- Delivery is at-least-once; OneSignal deduplicates repeat notifications.
  `delivered_at` is recorded **only** when the provider accepted the message —
  acceptance is not read-receipt, and the code never claims otherwise.
- Without credentials the no-op dispatcher logs and completes outbox rows;
  the in-app inbox still works and nothing is faked as delivered.

## Payload safety

Push payloads contain: title, body, `notification_id` and a `deep_link`
(`affoundation://requests/{id}`). Never: tokens, patient medical details,
phone numbers, or precise locations.

## Configuration steps (production)

1. Create a OneSignal app; add the Android (FCM) and iOS (APNs) platforms.
2. Set `OneSignal__AppId` and `OneSignal__ApiKey` (REST API key) on the API
   **and** the Worker containers.
3. Bake `ONESIGNAL_APP_ID` into the mobile apps at build time
   (`flutter build --dart-define=ONESIGNAL_APP_ID=...`).
4. The app initializes OneSignal only when an app id is present, requests
   notification permission politely after first sign-in, and registers the
   subscription with the API after login. Denied permission leaves the app
   fully usable (inbox mirrors the same data).

## Local development

Leave the variables empty — the no-op dispatcher logs each would-be push:

```
Push (no-op, OneSignal not configured): notification {id} -> 2 subscription(s): Blood needed near you
```

## Testing

The outbox pipeline (claim → send → retry → recover → skip-without-devices)
is covered against a fake `IPushDispatcher` in the integration suite. Real
device delivery is **not** verified in this repository — see
[known limitations](known-limitations.md).
