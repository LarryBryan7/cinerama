using Cinerama.Api.Auth;
using Cinerama.Application.Auth;
using Cinerama.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

/// <summary>Perfil del usuario autenticado. No recibe ningún id: la identidad sale del token (RN-34).</summary>
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(UserService users) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct) =>
        await users.GetProfileAsync(User.GetUserId(), ct);

    [HttpPut]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        await users.UpdateProfileAsync(User.GetUserId(), request, ct);
}
