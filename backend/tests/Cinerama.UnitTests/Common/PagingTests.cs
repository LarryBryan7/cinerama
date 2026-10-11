using Cinerama.Application.Common;
using FluentValidation;

namespace Cinerama.UnitTests.Common;

public class PagingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 20)]
    [InlineData(5, 100)]
    public void ValidValues_DoNotThrow(int page, int pageSize) =>
        Should.NotThrow(() => Paging.Validate(page, pageSize));

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void OutOfRange_ThrowsValidationException(int page, int pageSize, string field)
    {
        var ex = Should.Throw<ValidationException>(() => Paging.Validate(page, pageSize));
        ex.Errors.ShouldContain(e => e.PropertyName == field);
    }
}
