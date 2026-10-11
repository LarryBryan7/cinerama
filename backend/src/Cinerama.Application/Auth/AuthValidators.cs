using FluentValidation;

namespace Cinerama.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();

        // RN-02: al menos 8 caracteres, con una letra y un número. El máximo evita hashear entradas enormes.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(128)
            .Must(p => p is not null && p.Any(char.IsLetter)).WithMessage("La contraseña debe incluir al menos una letra.")
            .Must(p => p is not null && p.Any(char.IsDigit)).WithMessage("La contraseña debe incluir al menos un número.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
