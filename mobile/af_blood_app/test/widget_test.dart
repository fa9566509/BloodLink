import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:af_blood_app/app_router.dart';
import 'package:af_blood_app/features/auth/auth_repository.dart';

void main() {
  testWidgets('welcome screen renders for unauthenticated users', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);

    // The router redirects unauthenticated users to /welcome.
    final router = container.read(routerProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('AF Foundation'), findsWidgets);
    expect(find.text('Create an account'), findsOneWidget);
    expect(container.read(sessionProvider).isAuthenticated, isFalse);
  });
}
