namespace Xbim.WexServer.Contracts;

/// <summary>
/// Represents a user (external subject or local identity).
/// </summary>
public record UserDto
{
    public Guid Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}
