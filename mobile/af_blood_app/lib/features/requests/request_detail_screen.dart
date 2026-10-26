import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/models.dart';
import '../auth/auth_repository.dart';
import '../shared/states.dart';
import 'requests_repository.dart';
import 'requests_screens.dart';

class RequestDetailScreen extends ConsumerStatefulWidget {
  final String requestId;

  const RequestDetailScreen({super.key, required this.requestId});

  @override
  ConsumerState<RequestDetailScreen> createState() => _RequestDetailScreenState();
}

class _RequestDetailScreenState extends ConsumerState<RequestDetailScreen> {
  bool _working = false;

  Future<void> _respond(bool accept) async {
    setState(() => _working = true);
    try {
      await ref.read(requestsRepositoryProvider).respond(
          requestId: widget.requestId, accept: accept);
      ref.invalidate(requestDetailProvider(widget.requestId));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(accept
              ? 'Accepted — the requester has been notified to coordinate with you.'
              : 'Declined.'),
        ));
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _cancel() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Cancel this request?'),
        content: const Text(
            'It will stop accepting donors and stop generating notifications.'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Keep it')),
          FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Cancel request')),
        ],
      ),
    );
    if (confirmed != true) return;
    setState(() => _working = true);
    try {
      await ref.read(requestsRepositoryProvider).cancel(widget.requestId);
      ref.invalidate(requestDetailProvider(widget.requestId));
      ref.invalidate(myRequestsProvider);
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _reportDonation() async {
    final notes = TextEditingController();
    final date = await showDatePicker(
      context: context,
      initialDate: DateTime.now(),
      firstDate: DateTime.now().subtract(const Duration(days: 30)),
      lastDate: DateTime.now(),
      helpText: 'When was the donation made?',
    );
    if (date == null || !mounted) return;

    setState(() => _working = true);
    try {
      await ref.read(requestsRepositoryProvider).reportDonation(
            requestId: widget.requestId,
            donationDate: date,
          );
      ref.invalidate(requestDetailProvider(widget.requestId));
      ref.invalidate(myDonationsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text(
              'Donation reported. The requester has been asked to confirm it. '
              'A provisional restriction may apply until it is confirmed.'),
        ));
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) {
        notes.dispose();
        setState(() => _working = false);
      }
    }
  }

  Future<void> _acknowledge(bool acknowledge) async {
    setState(() => _working = true);
    try {
      await ref.read(requestsRepositoryProvider).acknowledge(
            requestId: widget.requestId,
            acknowledge: acknowledge,
          );
      ref.invalidate(requestDetailProvider(widget.requestId));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(acknowledge
              ? 'Acknowledged — thank you. The record stays marked as unverified '
                  'until the foundation verifies it.'
              : 'Disputed. The moderation team will review it.'),
        ));
      }
    } catch (error) {
      if (mounted) await showApiError(context, error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(requestDetailProvider(widget.requestId));

    return Scaffold(
      appBar: AppBar(title: const Text('Request details')),
      body: AsyncValueView<RequestDto>(
        value: detail,
        emptyMessage: 'Request not found.',
        onRetry: () => ref.invalidate(requestDetailProvider(widget.requestId)),
        builder: (context, r) {
          final isOwner = ref.watch(isOwnerProvider(r.requesterId));
          final isMatchedDonor = r.myResponseAccepted;

          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Row(
                children: [
                  CircleAvatar(
                    radius: 26,
                    child: Text(r.bloodGroup,
                        style: const TextStyle(
                            fontWeight: FontWeight.bold, fontSize: 18)),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(r.destinationLabel,
                            style: Theme.of(context).textTheme.titleMedium),
                        Text(
                          '${r.facilityName ?? 'Whole blood'} · '
                          '${r.urgency.toUpperCase()} · '
                          '${statusLabels[r.status] ?? r.status}',
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      _row('Requested by', r.requesterDisplayName),
                      _row('Created',
                          r.createdAtUtc.toLocal().toIso8601String().substring(0, 16)),
                      _row('Expires',
                          r.expiresAtUtc.toLocal().toIso8601String().substring(0, 16)),
                      if (r.referenceNote != null)
                        _row('Note', r.referenceNote!),
                      if (r.matchedDonorDisplayName != null)
                        _row('Matched donor', r.matchedDonorDisplayName!),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                'The donation and all medical screening happen at the '
                'donation centre. Accepting only signals your willingness to help.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 16),
              _actions(context, r, isOwner, isMatchedDonor),
            ],
          );
        },
      ),
    );
  }

  Widget _actions(
      BuildContext context, RequestDto r, bool isOwner, bool isMatchedDonor) {
    if (_working) {
      return const Center(child: CircularProgressIndicator());
    }

    final widgets = <Widget>[];

    if (isOwner) {
      if (r.status == 'open' || r.status == 'matched') {
        widgets.add(OutlinedButton.icon(
          icon: const Icon(Icons.cancel_outlined),
          label: const Text('Cancel request'),
          onPressed: _cancel,
        ));
      }
    } else {
      switch (r.status) {
        case 'open':
          widgets.add(FilledButton.icon(
            icon: const Icon(Icons.favorite),
            label: const Text('I can help — accept'),
            onPressed: () => _respond(true),
          ));
          widgets.add(const SizedBox(height: 8));
          widgets.add(OutlinedButton(
            onPressed: () => _respond(false),
            child: const Text('Not now'),
          ));
          break;
        case 'matched':
        case 'fulfilled':
          if (isMatchedDonor && r.status == 'matched') {
            widgets.add(FilledButton.icon(
              icon: const Icon(Icons.bloodtype),
              label: const Text('Report donation outcome'),
              onPressed: _reportDonation,
            ));
          }
          break;
      }
    }

    if (isOwner && r.status == 'fulfilled') {
      widgets.add(Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(children: [
            const Text(
                'The donor reported completing the donation. Please confirm or '
                'dispute this report.'),
            const SizedBox(height: 12),
            Wrap(spacing: 8, children: [
              FilledButton(
                onPressed: () => _acknowledge(true),
                child: const Text('Confirm'),
              ),
              OutlinedButton(
                onPressed: () => _acknowledge(false),
                child: const Text('Dispute'),
              ),
            ]),
          ]),
        ),
      ));
    }

    if (widgets.isEmpty) {
      return const SizedBox.shrink();
    }

    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: widgets);
  }

  Widget _row(String label, String value) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 110,
              child: Text(label,
                  style: const TextStyle(fontWeight: FontWeight.w600)),
            ),
            Expanded(child: Text(value)),
          ],
        ),
      );
}

/// Simple ownership check against the session user.
final isOwnerProvider = Provider.family<bool, String>((ref, requesterId) {
  final session = ref.watch(sessionProvider);
  return session.user?.id == requesterId;
});
