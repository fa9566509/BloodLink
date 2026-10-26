import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/models.dart';
import '../shared/states.dart';
import 'requests_repository.dart';

final nearbyRequestsProvider =
    FutureProvider.autoDispose<PagedResult<NearbyRequestDto>>(
        (ref) => ref.watch(requestsRepositoryProvider).nearby());

final myRequestsProvider =
    FutureProvider.autoDispose<PagedResult<RequestDto>>(
        (ref) => ref.watch(requestsRepositoryProvider).mine());

final myResponsesProvider = FutureProvider.autoDispose<PagedResult<ResponseSummary>>(
    (ref) => ref.watch(requestsRepositoryProvider).myResponses());

final myDonationsProvider = FutureProvider.autoDispose<PagedResult<DonationRecordDto>>(
    (ref) => ref.watch(requestsRepositoryProvider).myDonations());

final requestDetailProvider =
    FutureProvider.autoDispose.family<RequestDto, String>(
        (ref, id) => ref.watch(requestsRepositoryProvider).byId(id));

class NearbyRequestsScreen extends ConsumerWidget {
  const NearbyRequestsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final requests = ref.watch(nearbyRequestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Nearby requests')),
      floatingActionButton: FloatingActionButton.extended(
        icon: const Icon(Icons.add),
        label: const Text('New request'),
        onPressed: () => context.push('/create-request'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(nearbyRequestsProvider),
        child: AsyncValueView<PagedResult<NearbyRequestDto>>(
          value: requests,
          emptyMessage:
              'No open requests nearby right now that your blood group can help.',
          onRetry: () => ref.invalidate(nearbyRequestsProvider),
          builder: (context, page) => ListView.builder(
            physics: const AlwaysScrollableScrollPhysics(),
            itemCount: page.items.length,
            itemBuilder: (context, index) {
              final r = page.items[index];
              return Card(
                margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                child: ListTile(
                  leading: CircleAvatar(
                    backgroundColor:
                        urgencyColors[r.urgency]?.withValues(alpha: 0.15) ?? Colors.grey,
                    child: Text(r.bloodGroup,
                        style: TextStyle(
                            color: urgencyColors[r.urgency] ?? Colors.grey,
                            fontWeight: FontWeight.bold)),
                  ),
                  title: Text(r.destinationLabel),
                  subtitle: Text(
                      '${r.urgency.toUpperCase()} · ~${r.distanceKm} km · '
                      'expires ${_timeLeft(r.expiresAtUtc)}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.push('/requests/${r.requestId}'),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}

class MyRequestsScreen extends ConsumerWidget {
  const MyRequestsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final requests = ref.watch(myRequestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('My requests')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myRequestsProvider),
        child: AsyncValueView<PagedResult<RequestDto>>(
          value: requests,
          emptyMessage: 'You have not created any requests yet.',
          onRetry: () => ref.invalidate(myRequestsProvider),
          builder: (context, page) => ListView.builder(
            physics: const AlwaysScrollableScrollPhysics(),
            itemCount: page.items.length,
            itemBuilder: (context, index) {
              final r = page.items[index];
              return Card(
                margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                child: ListTile(
                  leading: CircleAvatar(
                    child: Text(r.bloodGroup,
                        style: const TextStyle(fontWeight: FontWeight.bold)),
                  ),
                  title: Text(r.destinationLabel),
                  subtitle: Text(
                      '${statusLabels[r.status] ?? r.status} · ${r.urgency.toUpperCase()}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.push('/requests/${r.id}'),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}

class MyResponsesScreen extends ConsumerWidget {
  const MyResponsesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final responses = ref.watch(myResponsesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('My responses')),
      body: AsyncValueView<PagedResult<ResponseSummary>>(
        value: responses,
        emptyMessage: 'You have not responded to any requests yet.',
        onRetry: () => ref.invalidate(myResponsesProvider),
        builder: (context, page) => ListView.builder(
          itemCount: page.items.length,
          itemBuilder: (context, index) {
            final r = page.items[index];
            final accepted = r.response == 'accepted';
            return ListTile(
              leading: Icon(
                accepted ? Icons.check_circle : Icons.cancel,
                color: accepted ? Colors.green : Colors.grey,
              ),
              title: Text(r.donorDisplayName),
              subtitle: Text(
                  '${accepted ? 'Accepted' : 'Declined'} · '
                  '${r.createdAtUtc.toLocal().toIso8601String().substring(0, 16)}'),
              trailing: TextButton(
                onPressed: () => context.push('/requests/${r.requestId}'),
                child: const Text('View'),
              ),
            );
          },
        ),
      ),
    );
  }
}

class DonationHistoryScreen extends ConsumerWidget {
  const DonationHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final donations = ref.watch(myDonationsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Donation history')),
      body: AsyncValueView<PagedResult<DonationRecordDto>>(
        value: donations,
        emptyMessage: 'No donations recorded yet.',
        onRetry: () => ref.invalidate(myDonationsProvider),
        builder: (context, page) => ListView.builder(
          itemCount: page.items.length,
          itemBuilder: (context, index) {
            final d = page.items[index];
            return ListTile(
              leading: Icon(Icons.bloodtype,
                  color: d.status == 'rejected' || d.status == 'disputed'
                      ? Colors.grey
                      : Colors.redAccent),
              title: Text(donationStatusLabels[d.status] ?? d.status),
              subtitle: Text(
                  'Donated on ${d.donationDate.toIso8601String().substring(0, 10)}'
                  '${d.eligibleAgainAtUtc != null ? ' · deferred until ${d.eligibleAgainAtUtc!.toIso8601String().substring(0, 10)}' : ''}'),
            );
          },
        ),
      ),
    );
  }
}

class CreateRequestScreen extends ConsumerStatefulWidget {
  const CreateRequestScreen({super.key});

  @override
  ConsumerState<CreateRequestScreen> createState() => _CreateRequestScreenState();
}

class _CreateRequestScreenState extends ConsumerState<CreateRequestScreen> {
  final _formKey = GlobalKey<FormState>();
  final _label = TextEditingController();
  final _facility = TextEditingController();
  final _note = TextEditingController();
  final _lat = TextEditingController();
  final _lon = TextEditingController();
  String _bloodGroup = 'O+';
  String _urgency = 'urgent';
  double _expiryHours = 48;
  bool _submitting = false;

  static const _bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

  @override
  void dispose() {
    _label.dispose();
    _facility.dispose();
    _note.dispose();
    _lat.dispose();
    _lon.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    final lat = double.tryParse(_lat.text.trim());
    final lon = double.tryParse(_lon.text.trim());
    if (lat == null || lon == null) {
      await showApiError(context, 'Enter valid destination coordinates.');
      return;
    }
    setState(() => _submitting = true);
    try {
      await ref.read(requestsRepositoryProvider).create(
            bloodGroup: _bloodGroup,
            latitude: lat,
            longitude: lon,
            destinationLabel: _label.text,
            facilityName: _facility.text.trim().isEmpty ? null : _facility.text,
            referenceNote: _note.text.trim().isEmpty ? null : _note.text,
            urgency: _urgency,
            expiresInHours: _expiryHours.round(),
          );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text(
              'Request created. Nearby compatible donors are being notified.'),
        ));
        context.go('/my-requests');
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('New blood request')),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('What blood group is needed?',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              children: _bloodGroups
                  .map((g) => ChoiceChip(
                        label: Text(g),
                        selected: _bloodGroup == g,
                        onSelected: (_) => setState(() => _bloodGroup = g),
                      ))
                  .toList(),
            ),
            const SizedBox(height: 16),
            Text('Urgency', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(value: 'routine', label: Text('Routine')),
                ButtonSegment(value: 'urgent', label: Text('Urgent')),
                ButtonSegment(value: 'critical', label: Text('Critical')),
              ],
              selected: {_urgency},
              onSelectionChanged: (s) => setState(() => _urgency = s.first),
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: _label,
              decoration: const InputDecoration(
                labelText: 'Where should the donor go? *',
                hintText: 'e.g. Block C, Services Hospital, Lahore',
              ),
              validator: (v) =>
                  v != null && v.trim().isNotEmpty ? null : 'Required',
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _facility,
              decoration: const InputDecoration(
                  labelText: 'Hospital / centre name (optional)'),
            ),
            const SizedBox(height: 12),
            Row(children: [
              Expanded(
                child: TextFormField(
                  controller: _lat,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(labelText: 'Latitude *'),
                  validator: (v) => double.tryParse(v ?? '') == null
                      ? 'Required'
                      : null,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: TextFormField(
                  controller: _lon,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(labelText: 'Longitude *'),
                  validator: (v) => double.tryParse(v ?? '') == null
                      ? 'Required'
                      : null,
                ),
              ),
            ]),
            const SizedBox(height: 12),
            TextFormField(
              controller: _note,
              decoration: const InputDecoration(
                labelText: 'Reference note (optional)',
                helperText:
                    'Non-sensitive coordination details only — never patient '
                    'medical data or contact numbers.',
              ),
              maxLength: 500,
            ),
            const SizedBox(height: 12),
            Text('Expires in ${_expiryHours.round()} hours'),
            Slider(
              value: _expiryHours,
              min: 1,
              max: 168,
              divisions: 167,
              label: '${_expiryHours.round()} h',
              onChanged: (v) => setState(() => _expiryHours = v),
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _submitting ? null : _submit,
              child: _submitting
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Create request'),
            ),
          ],
        ),
      ),
    );
  }
}

String _timeLeft(DateTime expiryUtc) {
  final diff = expiryUtc.difference(DateTime.now().toUtc());
  if (diff.isNegative) return 'expired';
  if (diff.inHours >= 24) return 'in ${diff.inDays} d';
  return 'in ${diff.inHours} h';
}
