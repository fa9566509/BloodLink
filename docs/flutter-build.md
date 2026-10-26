# Flutter build instructions

## Prerequisites

- Flutter SDK (stable) + platform tooling:
  - Android: Android Studio + SDK (emulator or device)
  - iOS: Xcode + CocoaPods (macOS only)
- The API reachable from the device/emulator.

## Run / build

```bash
cd mobile/af_blood_app
flutter pub get

# Android emulator → host machine API:
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:8080/api/v1
# iOS simulator:
flutter run --dart-define=API_BASE_URL=http://127.0.0.1:8080/api/v1
# Physical device on the LAN: use the host's LAN IP.

# Release builds (set the production API + OneSignal app id):
flutter build appbundle \
  --dart-define=API_BASE_URL=https://api.example.org/api/v1 \
  --dart-define=ONESIGNAL_APP_ID=<onesignal app id>
flutter build ipa \
  --dart-define=API_BASE_URL=https://api.example.org/api/v1 \
  --dart-define=ONESIGNAL_APP_ID=<onesignal app id>
```

## Quality gates

```bash
flutter analyze   # zero issues expected
flutter test      # widget smoke tests
```

Native builds (`flutter build appbundle` / `ipa`) require the Android SDK /
Xcode respectively — **they are not verified in this repository's CI**, see
[known limitations](known-limitations.md).

## Architecture

```
lib/
├── main.dart                 # OneSignal init (optional), ProviderScope
├── app.dart                  # MaterialApp.router, deep-link + auth side-effects
├── app_router.dart           # go_router: auth redirect, deep-link route
├── core/
│   ├── config.dart           # --dart-define configuration
│   ├── api_client.dart       # dio + bearer + transparent refresh, token storage
│   └── models.dart           # typed DTOs mirroring AF.Blood.Contracts
└── features/
    ├── auth/                 # welcome, login, register, recovery, verification
    ├── donor/                # profile, consent (GPS + manual fallback), eligibility
    ├── requests/             # nearby, create, detail (accept/decline/report), mine
    ├── donations/            # (history screen lives in requests_screens.dart)
    ├── notifications/        # inbox, devices
    ├── settings/             # account, push devices, sign out
    └── shell/                # bottom navigation shell
```

Conventions:
- State management: Riverpod (`FutureProvider` per screen data,
  `Notifier` for session/availability).
- No business rules in widgets: screens call repositories; eligibility,
  statuses and deferrals always come from the API.
- Every list/detail view renders through `AsyncValueView` — consistent
  loading, error (with retry), and empty states; offline connection errors
  produce actionable copy.
- Location permission is requested only on the consent screen, with a manual
  lat/lon fallback.
- The app is fully usable with notifications denied; the inbox shows the same
  data push would.

## Deep links

`affoundation://requests/{id}` — Android intent filter declared in
`AndroidManifest.xml`; warm-start links are handled via OneSignal click data
(`deep_link`), cold-start links via the OS launching the route directly.

## Known platform setup notes

- OneSignal requires its Android Gradle plugin + app id in the native
  projects when actually enabled; the Flutter plugin wires most of it, but
  follow OneSignal's current Flutter setup guide for the app id + FCM key
  ([onesignal-setup.md](onesignal-setup.md)).
- iOS: add push notifications capability + background modes (remote
  notifications) in Xcode when enabling OneSignal.
