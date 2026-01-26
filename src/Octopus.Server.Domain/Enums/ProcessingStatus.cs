namespace Octopus.Server.Domain.Enums;

/// <summary>
/// Processing status for a model version.
/// </summary>
public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3
}
