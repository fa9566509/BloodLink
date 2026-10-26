import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config.dart';
import '../auth/auth_repository.dart';

class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    final user = session.user;

    return Scaffold(
      appBar: AppBar(title: const Text('Account & settings')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: ListTile(
              leading: const Icon(Icons.person),
              title: Text(user?.displayName ?? '—'),
              subtitle: Text(user?.email ?? ''),
              trailing: user != null && !user.emailConfirmed
                  ? TextButton(
                      onPressed: () => context.push('/verify-email'),
                      child: const Text('Verify email'))
                  : const Icon(Icons.verified, color: Colors.green),
            ),
          ),
          const SizedBox(height: 8),
          Card(
            child: Column(children: [
              ListTile(
                leading: const Icon(Icons.notifications_outlined),
                title: const Text('Push devices'),
                subtitle: const Text(
                    'Manage where notifications are delivered'),
                onTap: () => context.push('/devices'),
              ),
              ListTile(
                leading: const Icon(Icons.history_edu),
                title: const Text('Donation history & deferral'),
                subtitle: const Text('Records and eligibility countdown'),
                onTap: () => context.push('/donations'),
              ),
              ListTile(
                leading: const Icon(Icons.shield_outlined),
                title: const Text('My responses'),
                subtitle: const Text('Requests you accepted or declined'),
                onTap: () => context.push('/my-responses'),
              ),
            ]),
          ),
          const SizedBox(height: 8),
          Card(
            child: Column(children: [
              ListTile(
                leading: const Icon(Icons.info_outline),
                title: const Text('About the service'),
                subtitle: const Text(
                    'AF Foundation coordinates discovery only. It does not '
                    'replace screening at the donation centre.'),
              ),
              ListTile(
                leading: const Icon(Icons.key),
                title: const Text('Consent version'),
                subtitle: const Text(AppConfig.consentVersion),
              ),
            ]),
          ),
          const SizedBox(height: 16),
          FilledButton.tonalIcon(
            icon: const Icon(Icons.logout),
            label: const Text('Sign out'),
            onPressed: () async {
              await ref.read(sessionProvider.notifier).signOut();
              if (context.mounted) context.go('/welcome');
            },
          ),
        ],
      ),
    );
  }
}
