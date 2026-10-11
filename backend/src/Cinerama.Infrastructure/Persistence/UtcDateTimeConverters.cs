using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cinerama.Infrastructure.Persistence;

/// <summary>
/// Todas las fechas se guardan en UTC (RN-38). SQL Server no conserva el <c>Kind</c>, así que al leer se marca
/// como UTC; sin esto, System.Text.Json serializaría las fechas sin la "Z" y los clientes las interpretarían
/// como hora local.
/// </summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => DateTime.SpecifyKind(v, DateTimeKind.Utc), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
