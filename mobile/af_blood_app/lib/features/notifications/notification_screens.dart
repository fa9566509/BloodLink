import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/models.dart';
import '../shared/states.dart';
import 'notifications_repository.dart';

final inboxProvider = FutureProvider.autoDispose<PagedResult<NotificationDto>>(
    (ref) => ref.watch(notificationsRepositoryProvider).list());

final devicesProvider = FutureProvider.autoDispose<List<DeviceDto>>(
    (ref) => ref.watch(notificationsRepositoryProvider).devices());

class InboxScreen extends ConsumerWidget {
  const InboxScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final inbox = ref.watch(inboxProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Notifications')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(inboxProvider),
        child: AsyncValueView<PagedResult<NotificationDto>>(
          value: inbox,
          emptyMessage: 'No notifications yet. When a nearby request matches '
              'you, it will appear here.',
          onRetry: () => ref.invalidate(inboxProvider),
          builder: (context, page) {
            // Mark visible unread rows read when the inbox opens.
            final unread = page.items.where((n) => n.readAtUtc == null)
                .map((n) => n.id).toList();
            if (unread.isNotEmpty) {
              Future(() async {
                await ref.read(notificationsRepositoryProvider).markRead(unread);
                ref.invalidate(inboxProvider);
              });
            }

            return ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: page.items.length,
              itemBuilder: (context, index) {
                final n = page.items[index];
                return ListTile(
                  leading: Icon(
                    _iconFor(n.type),
                    color: n.readAtUtc == null
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(context).colorScheme.outline,
                  ),
                  title: Text(n.title,
                      style: TextStyle(
                          fontWeight: n.readAtUtc == null
                              ? FontWeight.bold
                              : FontWeight.normal)),
                  subtitle: Text(
                      '${n.body}\n'
                      '${n.createdAtUtc.toLocal().toIso8601String().substring(0, 16)}'
                      '${n.deliveredAtUtc == null ? ' · push not delivered' : ''}'),
                  isThreeLine: true,
                  onTap: n.requestId == null
                      ? null
                      : () => context.push('/requests/${n.requestId}'),
                );
              },
            );
          },
        ),
      ),
    );
  }

  IconData _iconFor(String type) {
    switch (type) {
      case 'request_match_alert':
        return Icons.bloodtype;
      case 'donor_accepted':
        return Icons.favorite;
      case 'donation_reported':
        return Icons.fact_check;
      case 'donation_verified':
        return Icons.verified;
      case 'account_suspended':
      case 'account_restored':
        return Icons.admin_panel_settings;
      default:
        return Icons.notifications;
    }
  }
}

/// Registered OneSignal device subscriptions with disable action
/// (notification and privacy settings, spec §15.14).
class DevicesScreen extends ConsumerWidget {
  const DevicesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final devices = ref.watch(devicesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Push devices')),
      body: AsyncValueView<List<DeviceDto>>(
        value: devices,
        emptyMessage:
            'No devices registered. Notifications are sent to this device '
            'automatically after you log in (requires OneSignal setup).',
        onRetry: () => ref.invalidate(devicesProvider),
        builder: (context, list) => ListView.builder(
          itemCount: list.length,
          itemBuilder: (context, index) {
            final d = list[index];
            return ListTile(
              leading: Icon(_platformIcon(d.platform)),
              title: Text(
                  d.oneSignalSubscriptionId.substring(
                      0, d.oneSignalSubscriptionId.length.clamp(0, 24))),
              subtitle: Text(
                  '${d.platform} · ${d.isActive ? 'active' : 'disabled'} · '
                  'registered ${d.createdAtUtc.toLocal().toIso8601String().substring(0, 10)}'),
              trailing: d.isActive
                  ? TextButton(
                      onPressed: () async {
                        try {
                          await ref
                              .read(notificationsRepositoryProvider)
                              .disableDevice(d.id);
                          ref.invalidate(devicesProvider);
                        } catch (error) {
                          if (context.mounted) {
                            await showApiError(context, error);
                          }
                        }
                      },
                      child: const Text('Disable'),
                    )
                  : null,
            );
          },
        ),
      ),
    );
  }

  IconData _platformIcon(String platform) {
    switch (platform) {
      case 'android':
        return Icons.phone_android;
      case 'ios':
        return Icons.phone_iphone;
      default:
        return Icons.language;
    }
  }
}
