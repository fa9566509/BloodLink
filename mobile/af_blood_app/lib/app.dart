import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:onesignal_flutter/onesignal_flutter.dart';

import 'package:go_router/go_router.dart';

import 'app_router.dart';
import 'core/api_client.dart';
import 'features/auth/auth_repository.dart';
import 'core/config.dart';
import 'features/notifications/notifications_repository.dart';

/// Warm-start deep links coming from notification taps.
final deepLinkStream = StreamController<Uri>.broadcast();

class AfBloodApp extends ConsumerStatefulWidget {
  const AfBloodApp({super.key});

  @override
  ConsumerState<AfBloodApp> createState() => _AfBloodAppState();
}

class _AfBloodAppState extends ConsumerState<AfBloodApp> {
  late final GoRouter _router;
  StreamSubscription<Uri>? _deepLinks;

  @override
  void initState() {
    super.initState();
    _router = ref.read(routerProvider);

    // Warm-start deep links (cold-start ones arrive via the router's own
    // initial location handling).
    _deepLinks = deepLinkStream.stream.listen((uri) {
      handleDeepLink(uri, _router);
    });

    // React to auth state: register the push device after sign-in, and return
    // to the welcome screen when a session expires mid-use.
    ref.listenManual(sessionProvider, (previous, next) {
      if (next.isAuthenticated && previous?.isAuthenticated != true) {
        _requestPushPermissionAfterLogin();
        _registerDevice();
      }
    });

    ref.listenManual(authEventsProvider, (previous, next) {
      if (next == AuthEvent.sessionExpired) {
        ref.read(authEventsProvider.notifier).consumed();
        _router.go('/welcome');
      }
    });
  }

  @override
  void dispose() {
    _deepLinks?.cancel();
    super.dispose();
  }

  /// Permission is requested politely after first sign-in, never at startup,
  /// and a denial leaves the app fully usable (spec §15).
  void _requestPushPermissionAfterLogin() {
    if (AppConfig.oneSignalAppId.isEmpty) return;
    try {
      OneSignal.Notifications.requestPermission(true);
    } catch (_) {
      // OneSignal must never block the app.
    }
  }

  Future<void> _registerDevice() async {
    if (AppConfig.oneSignalAppId.isEmpty) return;
    try {
      final id = OneSignal.User.pushSubscription.id;
      if (id == null || id.isEmpty) return;
      await ref.read(notificationsRepositoryProvider).registerDevice(
            subscriptionId: id,
            platform: switch (defaultTargetPlatform) {
              TargetPlatform.android => 'android',
              TargetPlatform.iOS => 'ios',
              _ => 'web',
            },
          );
    } catch (_) {
      // Registration is retried at next sign-in; not user-facing.
    }
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'AF Foundation',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFFB91C1C)),
        useMaterial3: true,
      ),
      routerConfig: _router,
    );
  }
}
