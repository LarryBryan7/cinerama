using Cinerama.Domain.Entities;
using FluentValidation;

namespace Cinerama.Application.Catalog;

public sealed class UpsertReviewRequestValidator : AbstractValidator<UpsertReviewRequest>
{
    public UpsertReviewRequestValidator()
    {
        RuleFor(x => x.Rating)
            .InclusiveBetween(Review.MinRating, Review.MaxRating)
            .WithMessage($"La puntuación debe estar entre {Review.MinRating} y {Review.MaxRating}.");

        RuleFor(x => x.Comment)
            .MaximumLength(Review.MaxCommentLength).WithMessage($"El comentario no puede superar {Review.MaxCommentLength} caracteres.")
            // Solo texto: se admiten saltos de línea y tabulaciones, no otros caracteres de control.
            .Must(c => c!.All(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t'))
            .WithMessage("El comentario contiene caracteres no permitidos.")
            .When(x => x.Comment is not null);
    }
}
