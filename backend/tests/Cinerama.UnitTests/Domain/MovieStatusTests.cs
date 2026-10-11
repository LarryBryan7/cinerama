using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;

namespace Cinerama.UnitTests.Domain;

/// <summary>RN-41 y RN-42: el estado y la venta de una película se derivan de dos fechas y de "hoy".</summary>
public class MovieStatusTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Theory]
    [InlineData(-30, null, MovieStatus.NowShowing)]  // estrenada hace un mes
    [InlineData(0, null, MovieStatus.NowShowing)]    // estrena hoy: ya es cartelera
    [InlineData(0, -5, MovieStatus.NowShowing)]      // con preventa que ya cerró
    [InlineData(1, null, MovieStatus.ComingSoon)]    // mañana, sin preventa
    [InlineData(1, 1, MovieStatus.ComingSoon)]       // la preventa abre mañana: hoy todavía no
    [InlineData(9, 5, MovieStatus.ComingSoon)]
    [InlineData(1, 0, MovieStatus.PreSale)]          // la preventa abre hoy
    [InlineData(10, -3, MovieStatus.PreSale)]
    [InlineData(2, 2, MovieStatus.ComingSoon)]       // abre el mismo día del estreno, que es en 2 días
    public void StatusOn_DependsOnlyOnTheTwoDates(int releaseOffset, int? preSaleOffset, MovieStatus expected)
    {
        var movie = Movie(releaseOffset, preSaleOffset);

        movie.StatusOn(Today).ShouldBe(expected);
    }

    [Theory]
    [InlineData(-30, null, true)]
    [InlineData(0, null, true)]
    [InlineData(1, null, false)]   // sin preventa, no se vende antes del estreno
    [InlineData(1, 1, false)]      // la preventa abre mañana
    [InlineData(1, 0, true)]       // la preventa abre hoy
    [InlineData(10, -3, true)]
    public void IsSalesOpenOn_OpensWithThePreSale_OrWithThePremiereIfThereIsNone(int releaseOffset, int? preSaleOffset, bool expected) =>
        Movie(releaseOffset, preSaleOffset).IsSalesOpenOn(Today).ShouldBe(expected);

    [Fact]
    public void ThePreSale_AlwaysOpensTheSalesBeforeOrOnThePremiere()
    {
        // Para cualquier combinación válida (preventa <= estreno), cuando una película está en cartelera la venta está abierta.
        for (var release = -3; release <= 3; release++)
            for (var preSale = -5; preSale <= release; preSale++)
            {
                var movie = Movie(release, preSale);
                if (movie.StatusOn(Today) == MovieStatus.NowShowing)
                    movie.IsSalesOpenOn(Today).ShouldBeTrue($"estreno {release}, preventa {preSale}");
            }
    }

    private static Movie Movie(int releaseOffset, int? preSaleOffset) => new()
    {
        Title = "x", Classification = "+14",
        ReleaseDate = Today.AddDays(releaseOffset),
        PreSaleStartsOn = preSaleOffset is { } d ? Today.AddDays(d) : null,
    };
}
