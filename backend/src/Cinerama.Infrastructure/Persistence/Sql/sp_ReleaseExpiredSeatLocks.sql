-- Libera los bloqueos de asientos vencidos y devuelve cuáles liberó, para poder notificarlos (CU-06, ADR-005).
--
-- Por qué un stored procedure y no LINQ (ADR-009): UPDATE ... OUTPUT cambia y devuelve las filas en UNA sola
-- sentencia atómica. ExecuteUpdate de EF Core solo devuelve la cantidad de filas, y hacer SELECT y luego UPDATE
-- abre una carrera entre ambas sentencias: otro cliente podría bloquear el asiento entre las dos y recibiría
-- un aviso de "disponible" falso. Con dos instancias de la API ejecutando esto a la vez, cada fila la libera una
-- sola, así que no hay avisos duplicados y no hace falta ningún lock distribuido.
--
-- @NowUtc lo entrega la aplicación (su TimeProvider) en lugar de leer SYSUTCDATETIME(): así la aplicación y la
-- base usan UN solo reloj y las pruebas pueden adelantar el tiempo sin esperar 5 minutos.
-- @BatchSize acota cuántas filas se bloquean por ejecución; quien llama repite mientras el lote venga lleno.
CREATE PROCEDURE dbo.sp_ReleaseExpiredSeatLocks
    @NowUtc    datetime2(3),
    @BatchSize int = 500
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@BatchSize) dbo.ShowSeats
    SET    Status         = 'Available',
           LockedByUserId = NULL,
           LockedUntil    = NULL
    OUTPUT inserted.ShowtimeId,
           inserted.Id     AS ShowSeatId,
           inserted.SeatId AS SeatId
    WHERE  Status = 'Locked'
      AND  LockedUntil <= @NowUtc;   -- mismo criterio que ShowSeat.IsExpired; usa IX_ShowSeats_LockedUntil_Active
END
