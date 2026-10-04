using ComicStudio.Api.Auth;
using ComicStudio.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ComicStudio.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> users,
    JwtTokenService tokens,
    ILogger<AuthController> logger
) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationProblem("Email and password are required.");
        }

        var email = request.Email.Trim();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? email.Split('@')[0]
                : request.DisplayName.Trim(),
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(Describe(result));
        }

        logger.LogInformation("Registered user {UserId}.", user.Id);
        return Ok(CreateResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            // Deliberately identical response for unknown user and bad password.
            return Unauthorized(new ProblemDetails
            {
                Title = "Sign in failed",
                Detail = "That email and password combination was not recognised.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        return Ok(CreateResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        return user is null ? NotFound() : Ok(UserDto.From(user));
    }

    private AuthResponse CreateResponse(AppUser user)
    {
        var (token, expiresAt) = tokens.CreateToken(user);
        return new AuthResponse(token, expiresAt, UserDto.From(user));
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));
}
