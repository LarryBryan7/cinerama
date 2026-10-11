using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Common;
using Cinerama.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List(
        string? search, string? role, bool? isActive, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await users.ListAsync(search, role, isActive, page, pageSize, ct);

    /// <summary>Al desactivar una cuenta se revocan todos sus refresh tokens (RN-03).</summary>
    [HttpPut("{id:int}/status")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminUserDto>> SetStatus(int id, SetUserStatusRequest request, CancellationToken ct) =>
        await users.SetActiveAsync(User.GetUserId(), id, request.IsActive, ct);
}
