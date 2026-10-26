import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';

/// Consistent loading / error / empty states used by every list and detail
/// view (spec §15.16). `offline` distinguishes connection errors so users get
/// actionable copy.
class AsyncValueView<T> extends StatelessWidget {
  final AsyncValue<T> value;
  final Widget Function(BuildContext, T) builder;
  final String emptyMessage;
  final VoidCallback? onRetry;

  const AsyncValueView({
    super.key,
    required this.value,
    required this.builder,
    required this.emptyMessage,
    this.onRetry,
  });

  @override
  Widget build(BuildContext context) {
    return value.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (error, _) => ErrorStateView(
        message: describeApiError(error),
        onRetry: onRetry,
      ),
      data: (data) {
        if (_isEmpty(data)) {
          return EmptyStateView(message: emptyMessage);
        }
        return builder(context, data);
      },
    );
  }

  static bool _isEmpty(Object? data) {
    if (data is Iterable) return data.isEmpty;
    return false;
  }
}

class ErrorStateView extends StatelessWidget {
  final String message;
  final VoidCallback? onRetry;

  const ErrorStateView({super.key, required this.message, this.onRetry});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.cloud_off, size: 48, color: Colors.redAccent),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            if (onRetry != null) ...[
              const SizedBox(height: 12),
              FilledButton.tonal(
                onPressed: onRetry,
                child: const Text('Try again'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class EmptyStateView extends StatelessWidget {
  final String message;
  final IconData icon;

  const EmptyStateView({
    super.key,
    required this.message,
    this.icon = Icons.inbox_outlined,
  });

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 48, color: Theme.of(context).colorScheme.outline),
            const SizedBox(height: 12),
            Text(
              message,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ],
        ),
      ),
    );
  }
}

/// Shows a SnackBar with the API's ProblemDetails message on failure.
Future<void> showApiError(BuildContext context, Object error) {
  final messenger = ScaffoldMessenger.of(context);
  return messenger.showSnackBar(
    SnackBar(content: Text(describeApiError(error))),
  ).closed;
}

const urgencyColors = {
  'routine': Colors.green,
  'urgent': Colors.orange,
  'critical': Colors.red,
};

const statusLabels = {
  'open': 'Open',
  'matched': 'Matched',
  'fulfilled': 'Fulfilled',
  'cancelled': 'Cancelled',
  'expired': 'Expired',
  'under_review': 'Under review',
};

const donationStatusLabels = {
  'reported': 'Reported',
  'awaiting_confirmation': 'Awaiting confirmation',
  'completed_unverified': 'Completed (unverified)',
  'verified': 'Verified',
  'disputed': 'Disputed',
  'rejected': 'Rejected',
  'corrected': 'Corrected',
};
