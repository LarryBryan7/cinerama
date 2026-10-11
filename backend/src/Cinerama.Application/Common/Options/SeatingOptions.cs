using System.ComponentModel.DataAnnotations;

namespace Cinerama.Application.Common.Options;

/// <summary>Parámetros del bloqueo de asientos. Los valores por defecto son las reglas de negocio confirmadas.</summary>
public sealed class SeatingOptions
{
    public const string SectionName = "Seating";

    /// <summary>RN-19: duración del bloqueo.</summary>
    [Range(1, 60)]
    public int LockMinutes { get; set; } = 5;

    /// <summary>RN-20 y RN-24: máximo de asientos por cliente y función (bloqueados a la vez o por reserva).</summary>
    [Range(1, 20)]
    public int MaxSeatsPerUser { get; set; } = 10;

    /// <summary>Cada cuánto el proceso en segundo plano libera los bloqueos vencidos (CU-06).</summary>
    [Range(1, 3600)]
    public int CleanupSeconds { get; set; } = 30;

    /// <summary>
    /// Apaga el proceso de limpieza. La corrección del sistema no depende de él (RN-23): sin él solo se pierde la
    /// notificación de la liberación. Permite demostrarlo en las pruebas.
    /// </summary>
    public bool CleanupEnabled { get; set; } = true;

    public TimeSpan LockDuration => TimeSpan.FromMinutes(LockMinutes);
}
