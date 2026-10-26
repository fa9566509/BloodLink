import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../notifications/notification_screens.dart';
import '../settings/settings_screen.dart';

/// Bottom-navigation shell for the signed-in area.
class HomeShell extends StatelessWidget {
  final StatefulNavigationShell navigationShell;

  const HomeShell({super.key, required this.navigationShell});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: navigationShell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: navigationShell.currentIndex,
        onDestinationSelected: (index) => navigationShell.goBranch(
          index,
          initialLocation: index == navigationShell.currentIndex,
        ),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.bloodtype_outlined),
            selectedIcon: Icon(Icons.bloodtype),
            label: 'Nearby',
          ),
          NavigationDestination(
            icon: Icon(Icons.list_alt_outlined),
            selectedIcon: Icon(Icons.list_alt),
            label: 'Requests',
          ),
          NavigationDestination(
            icon: Icon(Icons.notifications_outlined),
            selectedIcon: Icon(Icons.notifications),
            label: 'Inbox',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Account',
          ),
        ],
      ),
    );
  }
}

class RequestsBranchScreen extends StatelessWidget {
  const RequestsBranchScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Requests')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: ListTile(
              leading: const Icon(Icons.my_location),
              title: const Text('My created requests'),
              subtitle: const Text('Track responses and outcomes'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/my-requests'),
            ),
          ),
          Card(
            child: ListTile(
              leading: const Icon(Icons.how_to_reg),
              title: const Text('My responses'),
              subtitle: const Text('What you accepted or declined'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/my-responses'),
            ),
          ),
          Card(
            child: ListTile(
              leading: const Icon(Icons.bloodtype),
              title: const Text('Donation history & deferral'),
              subtitle: const Text('Records and eligibility countdown'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/donations'),
            ),
          ),
          Card(
            child: ListTile(
              leading: const Icon(Icons.add_circle_outline),
              title: const Text('Create a blood request'),
              subtitle: const Text('Whole blood · single donor slot'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/create-request'),
            ),
          ),
        ],
      ),
    );
  }
}

class AccountBranchScreen extends StatelessWidget {
  const AccountBranchScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const SettingsScreen();
  }
}

class InboxBranchScreen extends StatelessWidget {
  const InboxBranchScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const InboxScreen();
  }
}
