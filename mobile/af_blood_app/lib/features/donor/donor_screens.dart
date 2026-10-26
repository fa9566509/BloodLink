import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';
import '../shared/states.dart';
import 'donor_repository.dart';

/// Provides the caller's donor profile (null when none exists yet).
final donorProfileProvider =
    FutureProvider.autoDispose<DonorProfileDto?>((ref) async {
  final repo = ref.watch(donorRepositoryProvider);
  final profile = await repo.me();
  ref.read(availabilityControllerProvider.notifier).syncFrom(profile);
  return profile;
});

final donorEligibilityProvider =
    FutureProvider.autoDispose<DonorEligibilityDto>(
        (ref) => ref.watch(donorRepositoryProvider).eligibility());

/// Mirrors availability locally so toggles feel instant while the API call
/// completes in the background.
class AvailabilityController extends Notifier<bool> {
  @override
  bool build() => false;

  void syncFrom(DonorProfileDto? profile) {
    if (profile != null) state = profile.isAvailable;
  }

  Future<void> toggle(bool value, BuildContext context) async {
    final previous = state;
    state = value;
    try {
      await ref.read(donorRepositoryProvider).setAvailability(isAvailable: value);
      ref.invalidate(donorProfileProvider);
      ref.invalidate(donorEligibilityProvider);
    } catch (error) {
      state = previous;
      if (context.mounted) await showApiError(context, error);
    }
  }
}

final availabilityControllerProvider =
    NotifierProvider<AvailabilityController, bool>(AvailabilityController.new);

class DonorProfileScreen extends ConsumerStatefulWidget {
  const DonorProfileScreen({super.key});

  @override
  ConsumerState<DonorProfileScreen> createState() => _DonorProfileScreenState();
}

class _DonorProfileScreenState extends ConsumerState<DonorProfileScreen> {
  static const _bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

  String? _bloodGroup;
  double _radius = 25;
  bool _saving = false;

  @override
  Widget build(BuildContext context) {
    final profileAsync = ref.watch(donorProfileProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Donor profile')),
      body: AsyncValueView<DonorProfileDto?>(
        value: profileAsync,
        emptyMessage: 'Create your donor profile to get started.',
        onRetry: () => ref.invalidate(donorProfileProvider),
        builder: (context, profile) {
          _bloodGroup ??= profile?.bloodGroup ?? _bloodGroups.first;
          _radius = (profile?.searchRadiusKm ?? 25).toDouble();

          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              if (profile == null) ...[
                Text(
                  'Welcome! Create your donor profile below. You can decide '
                  'later whether to make yourself discoverable.',
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                const SizedBox(height: 16),
              ] else ...[
                _ConsentCard(profile: profile),
                const SizedBox(height: 16),
                _AvailabilityCard(profile: profile),
                const SizedBox(height: 16),
                _EligibilityCard(),
                const SizedBox(height: 16),
              ],
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Blood group',
                          style: Theme.of(context).textTheme.titleMedium),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        children: _bloodGroups
                            .map((g) => ChoiceChip(
                                  label: Text(g),
                                  selected: _bloodGroup == g,
                                  onSelected: (_) =>
                                      setState(() => _bloodGroup = g),
                                ))
                            .toList(),
                      ),
                      const SizedBox(height: 16),
                      Text('Search radius preference: ${_radius.round()} km',
                          style: Theme.of(context).textTheme.titleMedium),
                      Slider(
                        value: _radius,
                        min: 1,
                        max: 100,
                        divisions: 99,
                        label: '${_radius.round()} km',
                        onChanged: (v) => setState(() => _radius = v),
                      ),
                      const SizedBox(height: 8),
                      FilledButton(
                        onPressed: _saving ? null : _save,
                        child: _saving
                            ? const SizedBox(
                                height: 20,
                                width: 20,
                                child: CircularProgressIndicator(strokeWidth: 2))
                            : Text(profile == null
                                ? 'Create profile'
                                : 'Save changes'),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  Future<void> _save() async {
    setState(() => _saving = true);
    try {
      await ref.read(donorRepositoryProvider).upsertProfile(
            bloodGroup: _bloodGroup!,
            searchRadiusKm: _radius.round(),
          );
      ref.invalidate(donorProfileProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Profile saved.')));
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }
}

class _ConsentCard extends ConsumerWidget {
  final DonorProfileDto profile;
  const _ConsentCard({required this.profile});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  profile.isDiscoverable ? Icons.visibility : Icons.visibility_off,
                  color: profile.isDiscoverable ? Colors.green : Colors.grey,
                ),
                const SizedBox(width: 8),
                Text(
                  profile.isDiscoverable
                      ? 'Discoverable by nearby requests'
                      : 'Discovery is off',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              profile.isDiscoverable
                  ? 'An approximate location (about 1 km area) is stored and used '
                      'only for matching. Other users never see your location.'
                  : 'Enable discovery to be notified when someone near you needs '
                      'your blood group. You can switch it off any time.',
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: () => profile.isDiscoverable
                  ? _withdraw(context, ref)
                  : context.push('/consent'),
              child: Text(profile.isDiscoverable
                  ? 'Switch off discovery'
                  : 'Enable discovery'),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _withdraw(BuildContext context, WidgetRef ref) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Switch off discovery?'),
        content: const Text(
            'You will stop being shown to new matching immediately and your '
            'stored approximate location will be deleted.'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel')),
          FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Switch off')),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await ref
          .read(donorRepositoryProvider)
          .setConsent(accept: false, latitude: null, longitude: null);
      ref.invalidate(donorProfileProvider);
      ref.invalidate(donorEligibilityProvider);
    } catch (error) {
      if (context.mounted) await showApiError(context, error);
    }
  }
}

class _AvailabilityCard extends ConsumerWidget {
  final DonorProfileDto profile;
  const _AvailabilityCard({required this.profile});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final availability = ref.watch(availabilityControllerProvider);
    return SwitchListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      title: const Text('Available to donate'),
      subtitle: Text(availability
          ? 'You can be matched and can accept requests.'
          : 'You will not appear in new matching while unavailable.'),
      value: availability,
      onChanged: (v) => ref
          .read(availabilityControllerProvider.notifier)
          .toggle(v, context),
    );
  }
}

class _EligibilityCard extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final eligibility = ref.watch(donorEligibilityProvider);
    return eligibility.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => ErrorStateView(
          message: describeApiError(e),
          onRetry: () => ref.invalidate(donorEligibilityProvider)),
      data: (data) => Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Eligibility', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              if (data.isDeferred) ...[
                Text(
                  'Deferred — eligible again in ${data.deferralRemainingDays ?? '?'} days',
                  style: TextStyle(
                      color: Colors.orange.shade800, fontWeight: FontWeight.w600),
                ),
                if (data.eligibleAgainAtUtc != null)
                  Text(
                      'Countdown ends: ${data.eligibleAgainAtUtc!.toLocal().toIso8601String().substring(0, 10)}'),
              ] else
                const Text('No active deferral.'),
              const SizedBox(height: 8),
              Text(data.message),
              const SizedBox(height: 8),
              Text(
                'Final eligibility is always determined by the donation centre.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Asks for location permission ONLY when the user chooses to enable
/// discovery, with a manual fallback (spec §15).
class ConsentScreen extends ConsumerStatefulWidget {
  const ConsentScreen({super.key});

  @override
  ConsumerState<ConsentScreen> createState() => _ConsentScreenState();
}

class _ConsentScreenState extends ConsumerState<ConsentScreen> {
  final _manualLat = TextEditingController();
  final _manualLon = TextEditingController();
  bool _submitting = false;
  String? _manualHint;

  @override
  void dispose() {
    _manualLat.dispose();
    _manualLon.dispose();
    super.dispose();
  }

  Future<void> _enableWithGps() async {
    setState(() => _submitting = true);
    try {
      LocationPermission permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        setState(() => _manualHint =
            'Location permission was not granted. Set your approximate area '
            'manually below instead.');
        return;
      }
      final position = await Geolocator.getCurrentPosition();
      await _submit(position.latitude, position.longitude);
    } catch (error) {
      setState(() => _manualHint =
          'Could not determine your location (${describeApiError(error)}). '
          'Set your approximate area manually below.');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _submit(double lat, double lon) async {
    try {
      await ref.read(donorRepositoryProvider).setConsent(
            accept: true,
            latitude: lat,
            longitude: lon,
          );
      ref.invalidate(donorProfileProvider);
      ref.invalidate(donorEligibilityProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(
              'Discovery enabled. The server stores your location rounded to '
              'about 1 km (${lat.toStringAsFixed(2)}, ${lon.toStringAsFixed(2)}).'),
        ));
        context.go('/donor-profile');
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Enable discovery')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const Text(
            'When discovery is on, an approximate location (rounded to about a '
            'one-kilometre area) is stored so we can find requests near you. '
            'Your precise location is never stored or shared. You can switch '
            'discovery off at any time.',
          ),
          const SizedBox(height: 16),
          FilledButton.icon(
            icon: const Icon(Icons.my_location),
            label: const Text('Use my current location'),
            onPressed: _submitting ? null : _enableWithGps,
          ),
          const SizedBox(height: 8),
          Text('— or set it manually —', textAlign: TextAlign.center),
          const SizedBox(height: 8),
          if (_manualHint != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(_manualHint!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ),
          TextField(
            controller: _manualLat,
            keyboardType:
                const TextInputType.numberWithOptions(decimal: true, signed: true),
            decoration: const InputDecoration(
                labelText: 'Latitude (e.g. 31.52)', hintText: '31.52'),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _manualLon,
            keyboardType:
                const TextInputType.numberWithOptions(decimal: true, signed: true),
            decoration: const InputDecoration(
                labelText: 'Longitude (e.g. 74.35)', hintText: '74.35'),
          ),
          const SizedBox(height: 12),
          OutlinedButton(
            onPressed: _submitting
                ? null
                : () {
                    final lat = double.tryParse(_manualLat.text.trim());
                    final lon = double.tryParse(_manualLon.text.trim());
                    if (lat == null ||
                        lon == null ||
                        lat < -90 ||
                        lat > 90 ||
                        lon < -180 ||
                        lon > 180) {
                      setState(() =>
                          _manualHint = 'Enter valid latitude and longitude.');
                      return;
                    }
                    _submit(lat, lon);
                  },
            child: const Text('Use this location'),
          ),
        ],
      ),
    );
  }
}
