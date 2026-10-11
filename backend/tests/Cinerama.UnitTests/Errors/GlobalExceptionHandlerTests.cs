using Cinerama.Api.Errors;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cinerama.UnitTests.Errors;

public class GlobalExceptionHandlerTests
{
    private static (int Status, string Code) Mapped(Exception ex)
    {
        var problem = GlobalExceptionHandler.Map(ex);
        return (problem.Status!.Value, (string)problem.Extensions["code"]!);
    }

    [Fact] public void NotFound_Is404() => Mapped(new NotFoundException("movie_not_found", "x")).ShouldBe((404, "movie_not_found"));
    [Fact] public void Conflict_Is409() => Mapped(new ConflictException("email_already_registered", "x")).ShouldBe((409, "email_already_registered"));
    [Fact] public void AuthenticationFailed_Is401() => Mapped(new AuthenticationFailedException("invalid_credentials", "x")).ShouldBe((401, "invalid_credentials"));
    [Fact] public void DomainRule_Is422() => Mapped(new DomainRuleException("sales_closed", "x")).ShouldBe((422, "sales_closed"));

    [Fact]
    public void ConcurrencyConflict_Is409() =>
        Mapped(new DbUpdateConcurrencyException("otra solicitud modificó la fila")).ShouldBe((409, "concurrency_conflict"));

    [Fact]
    public void Conflict_WithExtensions_SendsThemToTheClient()
    {
        var conflicts = new[] { new { seatId = 7, label = "F7" } };
        var problem = GlobalExceptionHandler.Map(new ConflictException("seat_unavailable", "x",
            new Dictionary<string, object?> { ["conflicts"] = conflicts }));

        problem.Status.ShouldBe(409);
        problem.Extensions["code"].ShouldBe("seat_unavailable");
        problem.Extensions["conflicts"].ShouldBe(conflicts);
    }

    [Fact]
    public void UniqueConstraintViolation_NotHandledByAService_Is409()
    {
        var ex = new UniqueConstraintViolationException("UX_Bookings_Code", "x", new Exception("inner"));

        Mapped(ex).ShouldBe((409, "unique_violation"));
    }

    [Fact]
    public void Validation_Is400_WithErrorsGroupedByCamelCaseField()
    {
        var ex = new ValidationException(
        [
            new ValidationFailure("FullName", "Obligatorio"),
            new ValidationFailure("Password", "Muy corta"),
            new ValidationFailure("Password", "Sin números"),
        ]);

        var problem = GlobalExceptionHandler.Map(ex);

        problem.Status.ShouldBe(400);
        problem.Extensions["code"].ShouldBe("validation_failed");
        var errors = (Dictionary<string, string[]>)problem.Extensions["errors"]!;
        errors["fullName"].ShouldBe(["Obligatorio"]);
        errors["password"].Length.ShouldBe(2);
    }

    [Fact]
    public void UnexpectedError_Is500_AndNeverLeaksInternalDetails()
    {
        var problem = GlobalExceptionHandler.Map(new InvalidOperationException("Server=db;Password=super-secret"));

        problem.Status.ShouldBe(500);
        problem.Extensions["code"].ShouldBe("internal_error");
        (problem.Detail + problem.Title).ShouldNotContain("super-secret");
        (problem.Detail + problem.Title).ShouldNotContain("Server=db");
    }

    [Fact]
    public async Task TryHandle_WritesTheProblemWithTheMatchingStatusCode()
    {
        ProblemDetailsContext? written = null;
        var service = new Mock<IProblemDetailsService>();
        service.Setup(s => s.WriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => written = c)
            .Returns(ValueTask.CompletedTask);
        var handler = new GlobalExceptionHandler(service.Object, NullLogger<GlobalExceptionHandler>.Instance);
        var http = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(http, new NotFoundException("showtime_not_found", "x"), CancellationToken.None);

        handled.ShouldBeTrue();
        http.Response.StatusCode.ShouldBe(404);
        written.ShouldNotBeNull();
        written.ProblemDetails.Extensions["code"].ShouldBe("showtime_not_found");
    }

    [Fact]
    public async Task TryHandle_WhenTheClientAbortedTheRequest_WritesNoBody()
    {
        var service = new Mock<IProblemDetailsService>(MockBehavior.Strict); // cualquier escritura haría fallar el test
        var handler = new GlobalExceptionHandler(service.Object, NullLogger<GlobalExceptionHandler>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };

        var handled = await handler.TryHandleAsync(http, new OperationCanceledException(), CancellationToken.None);

        handled.ShouldBeTrue();
        http.Response.StatusCode.ShouldBe(499);
    }
}
