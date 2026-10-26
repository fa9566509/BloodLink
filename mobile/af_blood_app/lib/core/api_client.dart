import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'config.dart';

/// Persists tokens in the platform secure storage (Keychain / Keystore-backed).
class TokenStorage {
  static const _accessKey = 'af_access_token';
  static const _refreshKey = 'af_refresh_token';

  final FlutterSecureStorage _storage;

  TokenStorage([FlutterSecureStorage? storage])
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  Future<void> save({required String accessToken, required String refreshToken}) async {
    await _storage.write(key: _accessKey, value: accessToken);
    await _storage.write(key: _refreshKey, value: refreshToken);
  }

  Future<String?> readAccessToken() => _storage.read(key: _accessKey);
  Future<String?> readRefreshToken() => _storage.read(key: _refreshKey);

  Future<void> clear() async {
    await _storage.delete(key: _accessKey);
    await _storage.delete(key: _refreshKey);
  }
}

final tokenStorageProvider = Provider<TokenStorage>((ref) => TokenStorage());

/// Central HTTP client: bearer-token injection and one transparent
/// refresh-and-retry on 401 (ADR: docs/authentication.md).
class ApiClient {
  final Dio dio;
  final TokenStorage tokens;
  final void Function()? onSessionExpired;

  ApiClient({
    required this.dio,
    required this.tokens,
    this.onSessionExpired,
  }) {
    dio.options
      ..baseUrl = AppConfig.apiBaseUrl
      ..connectTimeout = const Duration(seconds: 15)
      ..receiveTimeout = const Duration(seconds: 20);
    dio.interceptors.add(InterceptorsWrapper(
      onRequest: _onRequest,
      onError: _onError,
    ));
  }

  bool _refreshing = false;

  Future<void> _onRequest(options, RequestInterceptorHandler handler) async {
    final access = await tokens.readAccessToken();
    if (access != null) {
      options.headers['Authorization'] = 'Bearer $access';
    }
    handler.next(options);
  }

  Future<void> _onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final response = err.response;
    final isAuthCall = err.requestOptions.uri.path.contains('/auth/');

    if (response?.statusCode != 401 || isAuthCall || _refreshing) {
      handler.next(err);
      return;
    }

    final refresh = await tokens.readRefreshToken();
    if (refresh == null) {
      handler.next(err);
      return;
    }

    _refreshing = true;
    try {
      final refreshResponse = await Dio(BaseOptions(baseUrl: dio.options.baseUrl))
          .post('/auth/refresh', data: {'refreshToken': refresh});
      final access = refreshResponse.data['accessToken'] as String;
      final newRefresh = refreshResponse.data['refreshToken'] as String;
      await tokens.save(accessToken: access, refreshToken: newRefresh);

      // Replay the original request with the new token.
      err.requestOptions.headers['Authorization'] = 'Bearer $access';
      final replay = await Dio(BaseOptions(baseUrl: dio.options.baseUrl))
          .fetch(err.requestOptions);
      handler.resolve(replay);
    } on DioException {
      onSessionExpired?.call();
      handler.next(err);
    } finally {
      _refreshing = false;
    }
  }
}

final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient(
    dio: Dio(),
    tokens: ref.watch(tokenStorageProvider),
    onSessionExpired: () => ref.read(authEventsProvider.notifier).sessionExpired(),
  );
});

/// Signals from the network layer to the routing layer (e.g. refresh rejected
/// while a screen is open → return to login).
class AuthEvents extends Notifier<AuthEvent?> {
  @override
  AuthEvent? build() => null;

  void sessionExpired() => state = AuthEvent.sessionExpired;

  void consumed() => state = null;
}

enum AuthEvent { sessionExpired }

final authEventsProvider =
    NotifierProvider<AuthEvents, AuthEvent?>(AuthEvents.new);

/// Extracts a human-readable message from an API ProblemDetails body.
String describeApiError(Object error) {
  if (error is DioException) {
    final data = error.response?.data;
    if (data is Map<String, dynamic> && data['detail'] is String) {
      return data['detail'] as String;
    }
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return 'The connection timed out. Check your internet and try again.';
      case DioExceptionType.connectionError:
        return 'You appear to be offline. Reconnect and try again.';
      default:
        return 'Something went wrong (${error.response?.statusCode ?? 'network'}). Please try again.';
    }
  }
  return 'Something went wrong. Please try again.';
}
