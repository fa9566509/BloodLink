using AF.Blood.Application.Abstractions;

namespace AF.Blood.IntegrationTests.Support;

/// <summary>Captures every push dispatch so tests can assert exactly what the
/// system tried to deliver. Behaviour is scriptable for retry tests.</summary>
public sealed class FakePushDispatcher : IPushDispatcher
{
    private int _counter;
    private readonly List<PushMessage> _sent = [];
    private readonly Queue<PushDeliveryResult> _scripted = new();

    public IReadOnlyList<PushMessage> Sent => _sent;

    /// <summary>Queue a result for the next dispatch call (for failure tests).
    /// When the queue is empty the default success behaviour applies.</summary>
    public void Script(PushDeliveryResult result) => _scripted.Enqueue(result);

    public void Reset() => _sent.Clear();

    public Task<PushDeliveryResult> DispatchAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Add(message);
        var result = _scripted.Count > 0
            ? _scripted.Dequeue()
            : new PushDeliveryResult(true, $"os-{Interlocked.Increment(ref _counter)}", null);
        return Task.FromResult(result);
    }
}

/// <summary>Captures verification codes "sent" to users so tests can complete
/// the verification and recovery flows.</summary>
public sealed class FakeVerificationCodeSender : IVerificationCodeSender
{
    private readonly Dictionary<(string Purpose, string Destination), string> _codes = new();

    public string? LastCodeFor(string purpose, string destination) =>
        _codes.TryGetValue((purpose, destination), out var code) ? code : null;

    public Task SendAsync(VerificationPurpose purpose, string destination, string code, CancellationToken cancellationToken = default)
    {
        _codes[(purpose.ToString(), destination)] = code;
        return Task.CompletedTask;
    }
}
