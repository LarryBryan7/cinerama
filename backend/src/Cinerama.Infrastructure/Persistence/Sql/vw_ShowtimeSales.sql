-- Capacidad, vendidas, ingresos y ocupación por función. Es la ÚNICA definición de "vendida", "ingreso" y "ocupación" (RN-36, RN-37):
-- todos los reportes (y cualquier herramienta de BI) leen esta vista.
--   Vendida  = asiento de una reserva Confirmed que no fue liberado (ReleasedAt IS NULL).
--   Ingreso  = suma de los precios de esos asientos (el precio copiado al confirmar, RN-25). Las reservas canceladas no cuentan.
--   Ocupación = vendidas / capacidad, entre 0 y 1.
CREATE OR ALTER VIEW dbo.vw_ShowtimeSales
AS
SELECT
    s.Id                       AS ShowtimeId,
    s.MovieId                  AS MovieId,
    m.Title                    AS MovieTitle,
    c.Id                       AS CinemaId,
    c.Name                     AS CinemaName,
    s.CinemaRoomId             AS CinemaRoomId,
    r.Name                     AS RoomName,
    s.StartsAt                 AS StartsAt,
    s.Status                   AS Status,
    cap.Seats                  AS Capacity,
    ISNULL(sold.Seats, 0)      AS SeatsSold,
    ISNULL(sold.Revenue, 0)    AS Revenue,
    ISNULL(sold.Bookings, 0)   AS Bookings,
    CASE WHEN cap.Seats = 0 THEN CAST(0 AS decimal(9, 6))
         ELSE CAST(ISNULL(sold.Seats, 0) AS decimal(18, 6)) / cap.Seats END AS Occupancy
FROM dbo.Showtimes s
JOIN dbo.Movies       m ON m.Id = s.MovieId
JOIN dbo.CinemaRooms  r ON r.Id = s.CinemaRoomId
JOIN dbo.Cinemas      c ON c.Id = r.CinemaId
OUTER APPLY (SELECT COUNT(*) AS Seats FROM dbo.ShowSeats ss WHERE ss.ShowtimeId = s.Id) cap
OUTER APPLY (
    SELECT COUNT(*)            AS Seats,
           SUM(bs.Price)       AS Revenue,
           COUNT(DISTINCT b.Id) AS Bookings
    FROM dbo.Bookings b
    JOIN dbo.BookingSeats bs ON bs.BookingId = b.Id
    WHERE b.ShowtimeId = s.Id AND b.Status = 'Confirmed' AND bs.ReleasedAt IS NULL
) sold;
