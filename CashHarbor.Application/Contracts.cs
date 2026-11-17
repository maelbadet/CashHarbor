using CashHarbor.Domain;

namespace CashHarbor.Application;

public sealed class RuleException(string message) : Exception(message);
public interface IWorkspaceStore
{
    Task<Workspace> ReadAsync(Guid actor, CancellationToken ct = default);
    Task UpdateAsync(Guid actor, string action, Action<Workspace> change, CancellationToken ct = default);
}
public sealed record UserSession(Guid Id, string Email, string Version);
public sealed record SecurityEvent(DateTimeOffset At, string Action, Guid? Actor);
public interface IIdentityService
{
    Task<UserSession?> LoginAsync(string email, string password, string code, CancellationToken ct = default);
    Task<bool> IsValidAsync(Guid id, string version, CancellationToken ct = default);
    Task RevokeSessionsAsync(Guid id, CancellationToken ct = default);
    Task ProvisionAsync(string email, string password, string secret, string code, bool reset, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid id, string current, string password, string code, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityEvent>> EventsAsync(Guid actor, CancellationToken ct = default);
}
