import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';

class AuthRepository {
  final ApiClient client;
  AuthRepository(this.client);

  Future<AuthResponse> register({
    required String email,
    required String password,
    required String displayName,
    String? phoneNumber,
  }) async {
    final response = await client.dio.post('/auth/register', data: {
      'email': email.trim(),
      'password': password,
      'displayName': displayName.trim(),
      'phoneNumber': phoneNumber,
    });
    return AuthResponse.fromJson(response.data as Map<String, dynamic>);
  }

  Future<AuthResponse> login({required String email, required String password}) async {
    final response = await client.dio.post('/auth/login', data: {
      'email': email.trim(),
      'password': password,
    });
    return AuthResponse.fromJson(response.data as Map<String, dynamic>);
  }

  Future<void> logout(String refreshToken) =>
      client.dio.post('/auth/logout', data: {'refreshToken': refreshToken});

  Future<void> sendVerification(String purpose) =>
      client.dio.post('/auth/verification/request', data: {'purpose': purpose});

  Future<void> confirmVerification({required String purpose, required String code}) =>
      client.dio.post('/auth/verification/confirm',
          data: {'purpose': purpose, 'code': code.trim()});

  Future<void> forgotPassword(String email) =>
      client.dio.post('/auth/password/forgot', data: {'email': email.trim()});

  Future<void> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  }) =>
      client.dio.post('/auth/password/reset', data: {
        'email': email.trim(),
        'code': code.trim(),
        'newPassword': newPassword,
      });

  Future<UserDto> me() async {
    final response = await client.dio.get('/auth/me');
    return UserDto.fromJson(response.data as Map<String, dynamic>);
  }
}

final authRepositoryProvider =
    Provider<AuthRepository>((ref) => AuthRepository(ref.watch(apiClientProvider)));

/// Current session state consumed by the router and screens.
class SessionState {
  final UserDto? user;
  final bool loading;

  const SessionState({this.user, this.loading = false});

  bool get isAuthenticated => user != null;
}

class SessionController extends Notifier<SessionState> {
  @override
  SessionState build() {
    _restore();
    return const SessionState();
  }

  Future<void> _restore() async {
    final refresh = await client.tokens.readRefreshToken();
    if (refresh == null) return;
    state = const SessionState(loading: true);
    try {
      final user = await client.dio
          .get('/auth/me')
          .then((r) => UserDto.fromJson(r.data as Map<String, dynamic>));
      state = SessionState(user: user);
    } on DioException {
      await _signOutLocally();
    }
  }

  ApiClient get client => ref.read(apiClientProvider);

  Future<void> signedIn(AuthResponse auth) async {
    await client.tokens.save(
      accessToken: auth.accessToken,
      refreshToken: auth.refreshToken,
    );
    state = SessionState(user: auth.user);
  }

  Future<void> signOut() async {
    final refresh = await client.tokens.readRefreshToken();
    try {
      if (refresh != null) {
        await ref.read(authRepositoryProvider).logout(refresh);
      }
    } on DioException {
      // Even if the API call fails, the local session is dropped.
    } finally {
      await _signOutLocally();
    }
  }

  Future<void> _signOutLocally() async {
    await client.tokens.clear();
    state = const SessionState();
  }
}

final sessionProvider =
    NotifierProvider<SessionController, SessionState>(SessionController.new);
