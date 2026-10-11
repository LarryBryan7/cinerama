using System.ComponentModel.DataAnnotations;

namespace Cinerama.Application.Common.Options;

public sealed class CinemaOptions
{
    public const string SectionName = "Cinema";

    /// <summary>
    /// Zona horaria del cine (id IANA). Las fechas se guardan en UTC (RN-38); esta zona define qué es
    /// "el día X" al filtrar funciones y al agrupar reportes.
    /// </summary>
    [Required]
    public string TimeZone { get; set; } = "America/Lima";

    /// <summary>RN-30: el cliente cancela hasta estas horas antes de la función.</summary>
    [Range(0, 168)]
    public int CancellationHours { get; set; } = 2;

    /// <summary>RN-12: minutos de limpieza que se suman al final de cada función. Sale de aquí y no de una constante, para que la sala se pueda reorganizar sin recompilar.</summary>
    [Range(0, 120)]
    public int CleaningMinutes { get; set; } = 15;

    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}
