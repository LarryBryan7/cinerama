using Cinerama.Application.Common.Errors;

namespace Cinerama.UnitTests.Errors;

public class UniqueConstraintViolationExceptionTests
{
    private const string SqlMessage =
        "Cannot insert duplicate key row in object 'dbo.BookingSeats' with unique index 'UX_BookingSeats_ShowSeat_Active'. The duplicate key value is (12).";

    [Fact]
    public void Involves_MatchesTheParsedIndexName()
    {
        var ex = new UniqueConstraintViolationException("UX_BookingSeats_ShowSeat_Active", "x", new Exception(SqlMessage));

        ex.Involves("UX_BookingSeats_ShowSeat_Active").ShouldBeTrue();
        ex.Involves("ux_bookingseats_showseat_active").ShouldBeTrue();
        ex.Involves("UX_Users_Email").ShouldBeFalse();
    }

    [Fact]
    public void Involves_StillWorks_WhenTheServerMessageCouldNotBeParsed()
    {
        // Un SQL Server en otro idioma: no se pudo extraer el nombre, pero el nombre del índice sigue en el mensaje.
        var ex = new UniqueConstraintViolationException(null, "x", new Exception("No se puede insertar... índice único 'UX_Users_Email'..."));

        ex.Involves("UX_Users_Email").ShouldBeTrue();
        ex.Involves("UX_Bookings_Code").ShouldBeFalse();
    }
}
