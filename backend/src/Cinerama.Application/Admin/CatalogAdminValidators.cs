using Cinerama.Application.Catalog;
using FluentValidation;

namespace Cinerama.Application.Admin;

public sealed class GenreRequestValidator : AbstractValidator<GenreRequest>
{
    public GenreRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

public sealed class UpsertMovieRequestValidator : AbstractValidator<UpsertMovieRequest>
{
    public UpsertMovieRequestValidator()
    {
        // RN-09: título de 1 a 200 caracteres, duración de 1 a 400 minutos y al menos un género.
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Synopsis).MaximumLength(2000);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 400);
        RuleFor(x => x.Classification).NotEmpty().MaximumLength(20);
        RuleFor(x => x.GenreIds).NotNull().NotEmpty().WithMessage("La película necesita al menos un género.");
        RuleFor(x => x.GenreIds).Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("Hay géneros repetidos.");
        RuleFor(x => x.PosterUrl).Must(v => MediaValues.IsValid(v, MediaValues.ImageExtensions))
            .WithMessage("El póster debe ser una ruta relativa (posters/mi-pelicula.jpg) o una URL https de una imagen .jpg, .png o .webp.");
        RuleFor(x => x.PreviewVideoUrl).Must(v => MediaValues.IsValid(v, MediaValues.VideoExtensions))
            .WithMessage("El video corto debe ser una ruta relativa o una URL https de un archivo .mp4.");
        // RN-51: solo se acepta un código de YouTube válido.
        RuleFor(x => x.TrailerUrl).Must(v => string.IsNullOrWhiteSpace(v) || YouTubeVideoId.TryParse(v) is not null)
            .WithMessage("El tráiler debe ser un enlace de YouTube válido.");
        RuleFor(x => x.ReleaseDate).Must(d => d.Year >= 1900).WithMessage("La fecha de estreno no es válida.");
        // La preventa abre antes (o el mismo día) del estreno, nunca después (CHECK de la base).
        RuleFor(x => x.PreSaleStartsOn).Must((x, d) => d is null || d <= x.ReleaseDate)
            .WithMessage("La preventa no puede abrir después del estreno.");
    }
}

public sealed class UpsertCinemaRequestValidator : AbstractValidator<UpsertCinemaRequest>
{
    public UpsertCinemaRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m);
        RuleFor(x => x.LogoUrl).Must(v => MediaValues.IsValid(v, MediaValues.ImageExtensions))
            .WithMessage("El logo debe ser una ruta relativa (cinemas/mi-cine.png) o una URL https de una imagen.");
    }
}
