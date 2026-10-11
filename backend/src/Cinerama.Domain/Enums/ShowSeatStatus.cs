namespace Cinerama.Domain.Enums;

/// <summary>Estado de un asiento para una función concreta (ver docs/functional-design.md §1).</summary>
public enum ShowSeatStatus
{
    Available,
    Locked,
    Booked,
}
