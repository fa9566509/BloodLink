import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:onesignal_flutter/onesignal_flutter.dart';

import 'app.dart';
import 'core/config.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Push notifications initialise only when an app id is configured; the app
  // remains fully usable without them (in-app inbox mirrors the same data).
  if (AppConfig.oneSignalAppId.isNotEmpty) {
    try {
      OneSignal.initialize(AppConfig.oneSignalAppId);
      OneSignal.Notifications.addClickListener((event) {
        final data = event.notification.additionalData;
        final deepLink = data?['deep_link'];
        if (deepLink is String) {
          deepLinkStream.add(Uri.parse(deepLink));
        }
      });
      // Permission is requested politely after first sign-in, not at startup.
    } catch (_) {
      // OneSignal failures must never block the app.
    }
  }

  runApp(const ProviderScope(child: AfBloodApp()));
}
