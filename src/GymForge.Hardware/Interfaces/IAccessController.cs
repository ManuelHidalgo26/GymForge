namespace GymForge.Hardware.Interfaces;

public record DoorInfo(int DoorId, string Name, string ControllerAddress, bool IsOnline);
public record AccessEvent(int DoorId, string Credential, DateTime OccurredAt, bool AccessGranted);

public interface IAccessController
{
    Task OpenDoorAsync(int doorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoorInfo>> GetDoorsAsync(CancellationToken ct = default);
    IAsyncEnumerable<AccessEvent> GetEventStreamAsync(CancellationToken ct = default);
}
