import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';

class NotificationsRepository {
  final ApiClient client;
  NotificationsRepository(this.client);

  Future<PagedResult<NotificationDto>> list({int page = 1}) async {
    final response = await client.dio
        .get('/notifications', queryParameters: {'page': page, 'pageSize': 30});
    return PagedResult.fromJson(
        response.data as Map<String, dynamic>, NotificationDto.fromJson);
  }

  Future<int> unreadCount() async {
    final response = await client.dio.get('/notifications/unread-count');
    return (response.data as Map<String, dynamic>)['unread'] as int;
  }

  Future<void> markRead(List<String> ids) => ids.isEmpty
      ? Future.value()
      : client.dio.post('/notifications/mark-read', data: {'ids': ids});

  Future<DeviceDto> registerDevice({
    required String subscriptionId,
    required String platform,
  }) async {
    final response = await client.dio.post('/devices', data: {
      'oneSignalSubscriptionId': subscriptionId,
      'platform': platform,
    });
    return DeviceDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<List<DeviceDto>> devices() async {
    final response = await client.dio.get('/devices');
    return (response.data as List<dynamic>)
        .map((e) => DeviceDto.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<void> disableDevice(String deviceId) =>
      client.dio.post('/devices/$deviceId/disable');
}

final notificationsRepositoryProvider = Provider<NotificationsRepository>(
    (ref) => NotificationsRepository(ref.watch(apiClientProvider)));
