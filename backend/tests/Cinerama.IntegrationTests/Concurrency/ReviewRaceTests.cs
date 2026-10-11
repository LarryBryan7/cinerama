using System.Net;
using Cinerama.Application.Catalog;
using Cinerama.Infrastructure.Persistence;
using Cinerama.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Cinerama.IntegrationTests.Concurrency;

/// <summary>
/// La carrera de "dos primeras opiniones de la misma persona" no se puede provocar de forma fiable con hilos (la prueba con
/// ocho solicitudes simultáneas pasaba incluso sin el respaldo del índice único). Aquí se provoca de forma DETERMINISTA: justo
/// antes de que la API guarde su opinión, otra solicitud (simulada desde otra conexión) guarda la suya.
/// </summary>
public class ReviewRaceTests(ApiFixture fixture) : ReservationTest(fixture)
{
    private sealed class RaceInjector : SaveChangesInterceptor
    {
        private Func<Task>? _pending;

        public void BeforeNextSave(Func<Task> action) => _pending = action;
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Interlocked.Exchange(ref _pending, null);
            if (action is not null)
            {
                Fired = true;
                await action();
            }
            return result;
        }
    }

    [Fact]
    [Trait("Case", "RN-43")]
    public async Task WhenAnotherRequestSavesTheSameFirstReviewJustBefore_TheUniqueIndexIsRecoveredFrom_AndTheLastWriteWins()
    {
        var injector = new RaceInjector();
        await using var api = new TimedApi(Fixture, extraServices: services =>
            services.ConfigureDbContext<CineramaDbContext>(o => o.AddInterceptors(injector)));
        var movieId = await Db(async db => (await Seeds.MovieAsync(db, "Carrera")).Id);
        var ana = await NewUserAsync();

        injector.BeforeNextSave(async () =>
        {
            await using var connection = new SqlConnection(Fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "INSERT INTO Reviews (MovieId, UserId, Rating, Comment, CreatedAt, UpdatedAt) VALUES (@m, @u, 2, N'La otra solicitud', SYSUTCDATETIME(), SYSUTCDATETIME())", connection);
            command.Parameters.AddWithValue("@m", movieId);
            command.Parameters.AddWithValue("@u", ana.Id);
            await command.ExecuteNonQueryAsync();
        });

        var response = await Send(HttpMethod.Put, $"/api/v1/movies/{movieId}/reviews/mine", new { rating = 5, comment = "La mía" }, api.As(ana).Token, client: api.Client);

        injector.Fired.ShouldBeTrue("la carrera no se provocó: el test no prueba nada");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); // no es un 500: la otra ya existía, así que se actualizó
        (await Read<ReviewDto>(response)).Rating.ShouldBe(5);
        var rows = await Db(db => db.Reviews.ToListAsync());
        rows.Count.ShouldBe(1);
        rows.Single().Comment.ShouldBe("La mía"); // la última escritura gana, sin duplicados
        rows.Single().Rating.ShouldBe((byte)5);
    }
}
