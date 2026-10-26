/// Typed DTOs mirroring the AF.Blood.Contracts API models (camelCase JSON).
library;

class UserDto {
  final String id;
  final String email;
  final String displayName;
  final String? phoneNumber;
  final bool emailConfirmed;
  final List<String> roles;

  UserDto({
    required this.id,
    required this.email,
    required this.displayName,
    this.phoneNumber,
    required this.emailConfirmed,
    required this.roles,
  });

  factory UserDto.fromJson(Map<String, dynamic> j) => UserDto(
        id: j['id'] as String,
        email: j['email'] as String,
        displayName: j['displayName'] as String,
        phoneNumber: j['phoneNumber'] as String?,
        emailConfirmed: j['emailConfirmed'] as bool,
        roles: (j['roles'] as List<dynamic>? ?? []).cast<String>(),
      );
}

class AuthResponse {
  final String accessToken;
  final String refreshToken;
  final DateTime accessTokenExpiresAtUtc;
  final UserDto user;

  AuthResponse({
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresAtUtc,
    required this.user,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> j) => AuthResponse(
        accessToken: j['accessToken'] as String,
        refreshToken: j['refreshToken'] as String,
        accessTokenExpiresAtUtc: DateTime.parse(j['accessTokenExpiresAtUtc'] as String),
        user: UserDto.fromJson(j['user'] as Map<String, dynamic>),
      );
}

class DonorProfileDto {
  final String userId;
  final String bloodGroup;
  final bool isDiscoverable;
  final bool isAvailable;
  final double? approxLatitude;
  final double? approxLongitude;
  final int searchRadiusKm;
  final String? consentVersion;
  final DateTime? consentedAtUtc;
  final DateTime createdAtUtc;
  final DateTime updatedAtUtc;

  DonorProfileDto({
    required this.userId,
    required this.bloodGroup,
    required this.isDiscoverable,
    required this.isAvailable,
    this.approxLatitude,
    this.approxLongitude,
    required this.searchRadiusKm,
    this.consentVersion,
    this.consentedAtUtc,
    required this.createdAtUtc,
    required this.updatedAtUtc,
  });

  factory DonorProfileDto.fromJson(Map<String, dynamic> j) => DonorProfileDto(
        userId: j['userId'] as String,
        bloodGroup: j['bloodGroup'] as String,
        isDiscoverable: j['isDiscoverable'] as bool,
        isAvailable: j['isAvailable'] as bool,
        approxLatitude: (j['approxLatitude'] as num?)?.toDouble(),
        approxLongitude: (j['approxLongitude'] as num?)?.toDouble(),
        searchRadiusKm: j['searchRadiusKm'] as int,
        consentVersion: j['consentVersion'] as String?,
        consentedAtUtc: _date(j['consentedAtUtc']),
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
        updatedAtUtc: DateTime.parse(j['updatedAtUtc'] as String),
      );
}

class DonorEligibilityDto {
  final bool isDiscoverable;
  final bool isAvailable;
  final bool isDeferred;
  final String? deferralType;
  final DateTime? eligibleAgainAtUtc;
  final String? policyVersion;
  final int? deferralRemainingDays;
  final String message;

  DonorEligibilityDto({
    required this.isDiscoverable,
    required this.isAvailable,
    required this.isDeferred,
    this.deferralType,
    this.eligibleAgainAtUtc,
    this.policyVersion,
    this.deferralRemainingDays,
    required this.message,
  });

  factory DonorEligibilityDto.fromJson(Map<String, dynamic> j) =>
      DonorEligibilityDto(
        isDiscoverable: j['isDiscoverable'] as bool,
        isAvailable: j['isAvailable'] as bool,
        isDeferred: j['isDeferred'] as bool,
        deferralType: j['deferralType'] as String?,
        eligibleAgainAtUtc: _date(j['eligibleAgainAtUtc']),
        policyVersion: j['policyVersion'] as String?,
        deferralRemainingDays: j['deferralRemainingDays'] as int?,
        message: j['message'] as String,
      );
}

class NearbyRequestDto {
  final String requestId;
  final String bloodGroup;
  final String urgency;
  final String status;
  final String destinationLabel;
  final double approxLatitude;
  final double approxLongitude;
  final double distanceKm;
  final DateTime createdAtUtc;
  final DateTime expiresAtUtc;

  NearbyRequestDto({
    required this.requestId,
    required this.bloodGroup,
    required this.urgency,
    required this.status,
    required this.destinationLabel,
    required this.approxLatitude,
    required this.approxLongitude,
    required this.distanceKm,
    required this.createdAtUtc,
    required this.expiresAtUtc,
  });

  factory NearbyRequestDto.fromJson(Map<String, dynamic> j) => NearbyRequestDto(
        requestId: j['requestId'] as String,
        bloodGroup: j['bloodGroup'] as String,
        urgency: j['urgency'] as String,
        status: j['status'] as String,
        destinationLabel: j['destinationLabel'] as String,
        approxLatitude: (j['approxLatitude'] as num).toDouble(),
        approxLongitude: (j['approxLongitude'] as num).toDouble(),
        distanceKm: (j['distanceKm'] as num).toDouble(),
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
        expiresAtUtc: DateTime.parse(j['expiresAtUtc'] as String),
      );
}

class RequestDto {
  final String id;
  final String requesterId;
  final String requesterDisplayName;
  final String bloodGroup;
  final String componentType;
  final String destinationLabel;
  final double approxLatitude;
  final double approxLongitude;
  final String? facilityName;
  final String? referenceNote;
  final String urgency;
  final String status;
  final DateTime createdAtUtc;
  final DateTime expiresAtUtc;
  final String? matchedDonorUserId;
  final String? matchedDonorDisplayName;
  final double? distanceKm;
  final int acceptedCount;
  final bool myResponseAccepted;

  RequestDto({
    required this.id,
    required this.requesterId,
    required this.requesterDisplayName,
    required this.bloodGroup,
    required this.componentType,
    required this.destinationLabel,
    required this.approxLatitude,
    required this.approxLongitude,
    this.facilityName,
    this.referenceNote,
    required this.urgency,
    required this.status,
    required this.createdAtUtc,
    required this.expiresAtUtc,
    this.matchedDonorUserId,
    this.matchedDonorDisplayName,
    this.distanceKm,
    required this.acceptedCount,
    required this.myResponseAccepted,
  });

  factory RequestDto.fromJson(Map<String, dynamic> j) => RequestDto(
        id: j['id'] as String,
        requesterId: j['requesterId'] as String,
        requesterDisplayName: j['requesterDisplayName'] as String,
        bloodGroup: j['bloodGroup'] as String,
        componentType: j['componentType'] as String,
        destinationLabel: j['destinationLabel'] as String,
        approxLatitude: (j['approxLatitude'] as num).toDouble(),
        approxLongitude: (j['approxLongitude'] as num).toDouble(),
        facilityName: j['facilityName'] as String?,
        referenceNote: j['referenceNote'] as String?,
        urgency: j['urgency'] as String,
        status: j['status'] as String,
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
        expiresAtUtc: DateTime.parse(j['expiresAtUtc'] as String),
        matchedDonorUserId: j['matchedDonorUserId'] as String?,
        matchedDonorDisplayName: j['matchedDonorDisplayName'] as String?,
        distanceKm: (j['distanceKm'] as num?)?.toDouble(),
        acceptedCount: j['acceptedCount'] as int,
        myResponseAccepted: j['myResponseAccepted'] as bool,
      );
}

class NotificationDto {
  final String id;
  final String type;
  final String title;
  final String body;
  final String? deepLink;
  final String? requestId;
  final DateTime createdAtUtc;
  final DateTime? readAtUtc;
  final DateTime? deliveredAtUtc;

  NotificationDto({
    required this.id,
    required this.type,
    required this.title,
    required this.body,
    this.deepLink,
    this.requestId,
    required this.createdAtUtc,
    this.readAtUtc,
    this.deliveredAtUtc,
  });

  factory NotificationDto.fromJson(Map<String, dynamic> j) => NotificationDto(
        id: j['id'] as String,
        type: j['type'] as String,
        title: j['title'] as String,
        body: j['body'] as String,
        deepLink: j['deepLink'] as String?,
        requestId: j['requestId'] as String?,
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
        readAtUtc: _date(j['readAtUtc']),
        deliveredAtUtc: _date(j['deliveredAtUtc']),
      );
}

class DeviceDto {
  final String id;
  final String oneSignalSubscriptionId;
  final String platform;
  final bool isActive;
  final DateTime createdAtUtc;

  DeviceDto({
    required this.id,
    required this.oneSignalSubscriptionId,
    required this.platform,
    required this.isActive,
    required this.createdAtUtc,
  });

  factory DeviceDto.fromJson(Map<String, dynamic> j) => DeviceDto(
        id: j['id'] as String,
        oneSignalSubscriptionId: j['oneSignalSubscriptionId'] as String,
        platform: j['platform'] as String,
        isActive: j['isActive'] as bool,
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
      );
}

class DonationRecordDto {
  final String id;
  final String? requestId;
  final String donorUserId;
  final String reportedByUserId;
  final DateTime donationDate;
  final String status;
  final String? policyVersion;
  final DateTime? eligibleAgainAtUtc;
  final String? confirmedByUserId;
  final DateTime? confirmedAtUtc;
  final String? notes;
  final DateTime createdAtUtc;

  DonationRecordDto({
    required this.id,
    this.requestId,
    required this.donorUserId,
    required this.reportedByUserId,
    required this.donationDate,
    required this.status,
    this.policyVersion,
    this.eligibleAgainAtUtc,
    this.confirmedByUserId,
    this.confirmedAtUtc,
    this.notes,
    required this.createdAtUtc,
  });

  factory DonationRecordDto.fromJson(Map<String, dynamic> j) =>
      DonationRecordDto(
        id: j['id'] as String,
        requestId: j['requestId'] as String?,
        donorUserId: j['donorUserId'] as String,
        reportedByUserId: j['reportedByUserId'] as String,
        donationDate: DateTime.parse(j['donationDate'] as String),
        status: j['status'] as String,
        policyVersion: j['policyVersion'] as String?,
        eligibleAgainAtUtc: _date(j['eligibleAgainAtUtc']),
        confirmedByUserId: j['confirmedByUserId'] as String?,
        confirmedAtUtc: _date(j['confirmedAtUtc']),
        notes: j['notes'] as String?,
        createdAtUtc: DateTime.parse(j['createdAtUtc'] as String),
      );
}

class PagedResult<T> {
  final List<T> items;
  final int total;
  final int page;
  final int pageSize;

  PagedResult({
    required this.items,
    required this.total,
    required this.page,
    required this.pageSize,
  });

  factory PagedResult.fromJson(
    Map<String, dynamic> j,
    T Function(Map<String, dynamic>) fromJsonItem,
  ) =>
      PagedResult(
        items: (j['items'] as List<dynamic>)
            .map((e) => fromJsonItem(e as Map<String, dynamic>))
            .toList(),
        total: j['total'] as int,
        page: j['page'] as int,
        pageSize: j['pageSize'] as int,
      );
}

DateTime? _date(dynamic value) =>
    value is String ? DateTime.tryParse(value) : null;
