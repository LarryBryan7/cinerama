namespace Cinerama.Domain.Enums;

/// <summary>Estado de una película frente al público. Se deriva de las fechas, nunca se guarda (así no puede quedar desactualizado).</summary>
public enum MovieStatus
{
    /// <summary>Estreno futuro y la preventa todavía no abre: solo informativa, no se puede reservar.</summary>
    ComingSoon = 0,

    /// <summary>Estreno futuro pero la preventa ya abrió: se pueden reservar asientos.</summary>
    PreSale = 1,

    /// <summary>Ya se estrenó.</summary>
    NowShowing = 2,
}
