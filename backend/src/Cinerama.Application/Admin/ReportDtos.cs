namespace Cinerama.Application.Admin;

/// <summary>
/// KPIs de un periodo. El periodo se mide por la **fecha de la función** (cuándo se proyecta), en el calendario local del cine (RN-38).
/// <c>AverageOccupancy</c> va de 0 a 1 y es el promedio ponderado por capacidad (RN-36).
/// </summary>
public sealed record SalesSummaryDto(
    DateOnly From, DateOnly To, decimal Revenue, int Bookings, int TicketsSold, decimal AverageTicket, decimal AverageOccupancy, int Showtimes);

public sealed record DailySalesDto(DateOnly Date, decimal Revenue, int Bookings, int TicketsSold, int Showtimes, decimal Occupancy);

public sealed record MovieSalesDto(int MovieId, string Title, decimal Revenue, int Bookings, int TicketsSold, int Showtimes, decimal Occupancy);

/// <summary>
/// Una fila de ocupación. Por función: <c>Id</c> es la función, <c>Title</c> la película, <c>Subtitle</c> "cine · sala" y <c>StartsAt</c> su inicio.
/// Por sala: <c>Id</c> es la sala, <c>Title</c> "cine · sala" y <c>Showtimes</c> cuántas funciones tuvo en el periodo.
/// </summary>
public sealed record OccupancyRowDto(int Id, string Title, string? Subtitle, DateTime? StartsAt, int Capacity, int SeatsSold, decimal Occupancy, int Showtimes);
