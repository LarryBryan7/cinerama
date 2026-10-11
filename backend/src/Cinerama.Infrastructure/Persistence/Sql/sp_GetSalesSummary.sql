-- KPIs del dashboard para un rango [@FromUtc, @ToUtc) de inicios de función (instantes UTC; la API traduce las fechas locales del cine).
-- Una sola fila. Solo cuentan las funciones programadas: una cancelada no vende y su capacidad no debe diluir la ocupación.
--   AverageTicket    = ingresos / reservas (0 si no hay reservas).
--   AverageOccupancy = promedio ponderado por capacidad: vendidas / capacidad total (RN-36).
CREATE OR ALTER PROCEDURE dbo.sp_GetSalesSummary
    @FromUtc datetime2(3),
    @ToUtc   datetime2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CAST(ISNULL(SUM(Revenue), 0) AS decimal(18, 2))   AS Revenue,
        CAST(ISNULL(SUM(Bookings), 0) AS int)             AS Bookings,
        CAST(ISNULL(SUM(SeatsSold), 0) AS int)            AS TicketsSold,
        CAST(CASE WHEN ISNULL(SUM(Bookings), 0) = 0 THEN 0 ELSE SUM(Revenue) / SUM(Bookings) END AS decimal(18, 2)) AS AverageTicket,
        CAST(CASE WHEN ISNULL(SUM(Capacity), 0) = 0 THEN 0 ELSE CAST(SUM(SeatsSold) AS decimal(18, 6)) / SUM(Capacity) END AS decimal(9, 6)) AS AverageOccupancy,
        CAST(COUNT(*) AS int)                             AS Showtimes
    FROM dbo.vw_ShowtimeSales
    WHERE Status = 'Scheduled' AND StartsAt >= @FromUtc AND StartsAt < @ToUtc;
END
