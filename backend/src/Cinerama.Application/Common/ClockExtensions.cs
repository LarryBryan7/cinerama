namespace Cinerama.Application.Common;

public static class ClockExtensions
{
    /// <summary>
    /// Hora UTC actual truncada a milisegundos, que es la precisión de las columnas <c>datetime2(3)</c>. Sin esto, el
    /// valor que devuelve la API al crear algo (con 7 decimales) no coincide con el que queda guardado y se lee después.
    /// </summary>
    public static DateTime UtcNowMs(this TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    /// <summary>
    /// La fecha de hoy en el calendario local del cine (RN-38). El estreno, la preventa y "hoy" de las reseñas se miden con
    /// esta fecha y no con la del servidor, que puede estar en otra zona horaria.
    /// </summary>
    public static DateOnly LocalToday(this TimeProvider time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
}
