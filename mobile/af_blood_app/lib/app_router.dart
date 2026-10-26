import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/config.dart';
import 'features/auth/auth_repository.dart';
import 'features/auth/auth_screens.dart';
import 'features/donor/donor_screens.dart';
import 'features/notifications/notification_screens.dart';
import 'features/requests/request_detail_screen.dart';
import 'features/requests/requests_screens.dart';
import 'features/shell/home_shell.dart';

final routerProvider = Provider<GoRouter>((ref) {
  final session = ref.watch(sessionProvider);

  return GoRouter(
    initialLocation: '/nearby',
    debugLogDiagnostics: false,
    redirect: (context, state) {
      final authenticated = session.isAuthenticated;
      final loggingIn = state.matchedLocation == '/welcome' ||
          state.matchedLocation == '/login' ||
          state.matchedLocation == '/register' ||
          state.matchedLocation == '/recovery';

      if (!authenticated) {
        return loggingIn ? null : '/welcome';
      }
      if (loggingIn) return '/nearby';
      return null;
    },
    routes: [
      GoRoute(
        path: '/welcome',
        builder: (context, state) => const WelcomeScreen(),
      ),
      GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
      GoRoute(
          path: '/register', builder: (context, state) => const RegisterScreen()),
      GoRoute(
          path: '/recovery', builder: (context, state) => const RecoveryScreen()),
      GoRoute(
          path: '/verify-email',
          builder: (context, state) => const VerificationScreen()),
      GoRoute(
          path: '/consent', builder: (context, state) => const ConsentScreen()),
      GoRoute(
        path: '/requests/:id',
        // Deep-link destination: affoundation://requests/{id} (cold start).
        builder: (context, state) =>
            RequestDetailScreen(requestId: state.pathParameters['id']!),
      ),
      GoRoute(
          path: '/create-request',
          builder: (context, state) => const CreateRequestScreen()),
      GoRoute(
          path: '/my-requests',
          builder: (context, state) => const MyRequestsScreen()),
      GoRoute(
          path: '/my-responses',
          builder: (context, state) => const MyResponsesScreen()),
      GoRoute(
          path: '/donations',
          builder: (context, state) => const DonationHistoryScreen()),
      GoRoute(
          path: '/devices', builder: (context, state) => const DevicesScreen()),
      GoRoute(
        path: '/donor-profile',
        builder: (context, state) => const DonorProfileScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            HomeShell(navigationShell: navigationShell),
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/nearby',
                builder: (context, state) => const NearbyRequestsScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/requests-branch',
                builder: (context, state) => const RequestsBranchScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/inbox',
                builder: (context, state) => const InboxBranchScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/account',
                builder: (context, state) => const AccountBranchScreen(),
              ),
            ],
          ),
        ],
      ),
    ],
    errorBuilder: (context, state) => Scaffold(
      appBar: AppBar(title: const Text('Not found')),
      body: Center(child: Text('Page not found: ${state.uri}')),
    ),
  );
});

/// Navigates notification deep links (affoundation://requests/{id}) to the
/// in-app request detail. Warm start only; cold-start links open the same
/// route through the app's launch intent.
void handleDeepLink(Uri uri, GoRouter router) {
  if (uri.scheme != AppConfig.deepLinkScheme) return;
  if (uri.host == 'requests' && uri.path.isNotEmpty) {
    router.push('/requests/${uri.path}');
  }
}
