/// Build-time configuration. Override with --dart-define:
///   flutter run --dart-define=API_BASE_URL=https://api.example.org/api/v1
///   flutter run --dart-define=ONESIGNAL_APP_ID=xxxxxxxx
///
/// The default points at the Android emulator's loopback alias for a locally
/// running API (docker compose exposes it on port 8080). iOS simulators use
/// http://127.0.0.1:8080/api/v1 instead.
class AppConfig {
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:8080/api/v1',
  );

  static const oneSignalAppId = String.fromEnvironment(
    'ONESIGNAL_APP_ID',
    defaultValue: '',
  );

  /// Deep-link scheme used by the API's push notifications
  /// (affoundation://requests/{id}).
  static const deepLinkScheme = 'affoundation';

  static const consentVersion = '2026-10-v1';
}
