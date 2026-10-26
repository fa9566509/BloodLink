import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';

class RequestsRepository {
  final ApiClient client;
  RequestsRepository(this.client);

  Future<PagedResult<NearbyRequestDto>> nearby({int page = 1}) async {
    final response = await client.dio
        .get('/requests/nearby', queryParameters: {'page': page, 'pageSize': 20});
    return PagedResult.fromJson(
        response.data as Map<String, dynamic>, NearbyRequestDto.fromJson);
  }

  Future<PagedResult<RequestDto>> mine({int page = 1}) async {
    final response = await client.dio
        .get('/requests/mine', queryParameters: {'page': page, 'pageSize': 20});
    return PagedResult.fromJson(
        response.data as Map<String, dynamic>, RequestDto.fromJson);
  }

  Future<PagedResult<ResponseSummary>> myResponses({int page = 1}) async {
    final response = await client.dio
        .get('/responses/mine', queryParameters: {'page': page, 'pageSize': 20});
    return PagedResult.fromJson(
        response.data as Map<String, dynamic>, ResponseSummary.fromJson);
  }

  Future<RequestDto> byId(String id) async {
    final response = await client.dio.get('/requests/$id');
    return RequestDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<void> create({
    required String bloodGroup,
    required double latitude,
    required double longitude,
    required String destinationLabel,
    String? facilityName,
    String? referenceNote,
    required String urgency,
    int? expiresInHours,
  }) async {
    await client.dio.post('/requests', data: {
      'bloodGroup': bloodGroup,
      'latitude': latitude,
      'longitude': longitude,
      'destinationLabel': destinationLabel.trim(),
      'facilityName': facilityName,
      'referenceNote': referenceNote,
      'urgency': urgency,
      'expiresInHours': expiresInHours,
    });
  }

  Future<void> respond({required String requestId, required bool accept}) async {
    await client.dio.post('/requests/$requestId/responses',
        data: {'response': accept ? 'accepted' : 'declined'});
  }

  Future<void> cancel(String requestId) =>
      client.dio.post('/requests/$requestId/cancel');

  Future<DonationRecordDto> reportDonation({
    required String requestId,
    required DateTime donationDate,
    String? notes,
  }) async {
    final response = await client.dio
        .post('/requests/$requestId/donation-report', data: {
      'donationDate': donationDate.toIso8601String().substring(0, 10),
      'notes': notes,
    });
    return DonationRecordDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<DonationRecordDto> acknowledge({
    required String requestId,
    required bool acknowledge,
    String? note,
  }) async {
    final response = await client.dio
        .post('/requests/$requestId/donation-acknowledgement',
            data: {'acknowledge': acknowledge, 'note': note});
    return DonationRecordDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// The detail endpoint does not embed the donation record; fetch the
  /// donor's own history and match on the request id.
  Future<DonationRecordDto?> donationForRequest(String requestId) async {
    final mine = await myDonations();
    for (final record in mine.items) {
      if (record.requestId == requestId) return record;
    }
    return null;
  }

  Future<PagedResult<DonationRecordDto>> myDonations({int page = 1}) async {
    final response = await client.dio
        .get('/donations/mine', queryParameters: {'page': page, 'pageSize': 50});
    return PagedResult.fromJson(
        response.data as Map<String, dynamic>, DonationRecordDto.fromJson);
  }
}

class ResponseSummary {
  final String id;
  final String requestId;
  final String donorUserId;
  final String donorDisplayName;
  final String response;
  final DateTime createdAtUtc;

  ResponseSummary({
    required this.id,
    required this.requestId,
    required this.donorUserId,
    required this.donorDisplayName,
    required this.response,
    required this.createdAtUtc,
  });

  factory ResponseSummary.fromJson(Map<String, dynamic> j) => ResponseSummary(
        id: j['id'] as String,
        requestId: j['requestId'] as String,
        donorUserId: j['donorUserId'] as String,
        donorDisplayName: j['donorDisplayName'] as String,
        response: j['response'] as String,
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
      );
}

final requestsRepositoryProvider = Provider<RequestsRepository>(
    (ref) => RequestsRepository(ref.watch(apiClientProvider)));
