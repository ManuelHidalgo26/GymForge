namespace GymForge.Hardware.Interfaces;

public record EnrollResult(byte[] Template, float Quality);
public record VerifyResult(bool Matched, float Score);
public record BiometricSwipeEvent(Guid DeviceId, byte[] Template, DateTime OccurredAt);

public interface IBiometricReader
{
    /// <summary>Enroll a fingerprint (3 captures internally). Returns binary ZKTeco template.</summary>
    Task<EnrollResult> EnrollAsync(Guid memberId, int fingerIndex, CancellationToken ct = default);

    /// <summary>One-to-one verify against a stored template.</summary>
    Task<VerifyResult> VerifyAsync(byte[] storedTemplate, CancellationToken ct = default);

    /// <summary>Stream of swipe events from network ZKTeco terminals.</summary>
    IAsyncEnumerable<BiometricSwipeEvent> GetSwipeStreamAsync(CancellationToken ct = default);
}
