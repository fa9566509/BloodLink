import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';

class DonorRepository {
  final ApiClient client;
  DonorRepository(this.client);

  Future<DonorProfileDto?> me() async {
    try {
      final response = await client.dio.get('/donors/me');
      return DonorProfileDto.fromJson(response.data as Map<String, dynamic>);
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  Future<DonorProfileDto> upsertProfile({
    required String bloodGroup,
    required int searchRadiusKm,
  }) async {
    final response = await client.dio.put('/donors/me', data: {
      'bloodGroup': bloodGroup,
      'searchRadiusKm': searchRadiusKm,
    });
    return DonorProfileDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<DonorProfileDto> setConsent({
    required bool accept,
    double? latitude,
    double? longitude,
  }) async {
    final response = await client.dio.put('/donors/me/consent', data: {
      'accept': accept,
      'consentVersion': consentVersion,
      'latitude': latitude,
      'longitude': longitude,
    });
    return DonorProfileDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<DonorProfileDto> setAvailability({required bool isAvailable}) async {
    final response = await client.dio.put('/donors/me/availability',
        data: {'isAvailable': isAvailable});
    return DonorProfileDto.fromJson(response.data as Map<String, dynamic>);
  }

  Future<DonorEligibilityDto> eligibility() async {
    final response = await client.dio.get('/donors/me/eligibility');
    return DonorEligibilityDto.fromJson(response.data as Map<String, dynamic>);
  }
}

const consentVersion = '2026-10-v1';

final donorRepositoryProvider =
    Provider<DonorRepository>((ref) => DonorRepository(ref.watch(apiClientProvider)));
