using ComicStudio.Api.Auth;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Features.Auth;
using ComicStudio.Api.NovelAi;
using ComicStudio.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ComicStudio.Api.Features.Settings;

[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController(
    UserManager<AppUser> users,
    INovelAiClient novelAi,
    SecretProtector protector,
    ILogger<SettingsController> logger
) : ControllerBase
{
    [HttpGet("novelai")]
    public async Task<ActionResult<NovelAiConnectionDto>> GetNovelAi()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        return user is null ? NotFound() : Ok(Describe(user));
    }

    /// <summary>Validates a persistent token against NovelAI, then stores it encrypted.</summary>
    [HttpPut("novelai")]
    public async Task<ActionResult<NovelAiConnectionDto>> ConnectNovelAi(
        ConnectNovelAiRequest request,
        CancellationToken cancellationToken
    )
    {
        var token = request.Token?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return ValidationProblem("A NovelAI persistent token is required.");
        }

        var probe = await novelAi.GetAccountAsync(token, cancellationToken);
        if (probe.Status != NovelAiProbeStatus.Ok || probe.Account is null)
        {
            logger.LogInformation(
                "NovelAI connection attempt failed ({Status}): {Message}",
                probe.Status,
                probe.Message
            );

            return BadRequest(
                new ProblemDetails
                {
                    Title = "NovelAI connection failed",
                    Detail = probe.Message ?? "NovelAI rejected that token.",
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }

        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
        {
            return NotFound();
        }

        user.NovelAiTokenCipher = protector.Protect(user.Id, token);
        user.NovelAiTokenLast4 = token.Length >= 4 ? token[^4..] : null;
        Apply(user, probe.Account);
        await users.UpdateAsync(user);

        logger.LogInformation("User {UserId} connected a NovelAI account.", user.Id);
        return Ok(Describe(user));
    }

    [HttpPost("novelai/refresh")]
    public async Task<ActionResult<NovelAiConnectionDto>> Refresh(CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
        {
            return NotFound();
        }

        var token = protector.Unprotect(user.Id, user.NovelAiTokenCipher);
        if (string.IsNullOrEmpty(token))
        {
            return Ok(new NovelAiConnectionDto(
                Connected: false,
                TokenLast4: user.NovelAiTokenLast4,
                Tier: NovelAiTier.Unknown,
                SubscriptionActive: false,
                AnlasBalance: null,
                CheckedAt: user.NovelAiCheckedAt,
                Message: "Stored token could not be read. Reconnect your NovelAI account."
            ));
        }

        var probe = await novelAi.GetAccountAsync(token, cancellationToken);
        if (probe.Status == NovelAiProbeStatus.Ok && probe.Account is not null)
        {
            Apply(user, probe.Account);
        }
        else
        {
            user.NovelAiCheckedAt = DateTimeOffset.UtcNow;
        }

        await users.UpdateAsync(user);
        return Ok(Describe(user, probe.Message));
    }

    [HttpDelete("novelai")]
    public async Task<ActionResult<NovelAiConnectionDto>> DisconnectNovelAi()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
        {
            return NotFound();
        }

        user.NovelAiTokenCipher = null;
        user.NovelAiTokenLast4 = null;
        user.NovelAiTier = NovelAiTier.Unknown;
        user.NovelAiSubscriptionActive = false;
        user.AnlasBalance = null;
        user.NovelAiCheckedAt = null;
        await users.UpdateAsync(user);

        logger.LogInformation("User {UserId} disconnected their NovelAI account.", user.Id);
        return Ok(Describe(user));
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<UserDto>> UpdatePreferences(UserPreferencesRequest request)
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
        {
            return NotFound();
        }

        user.PreferredTextModelId = request.PreferredTextModelId;
        user.PreferredImageModelId = request.PreferredImageModelId;
        await users.UpdateAsync(user);

        return Ok(UserDto.From(user));
    }

    private static void Apply(AppUser user, NovelAiAccount account)
    {
        user.NovelAiTier = account.Tier;
        user.NovelAiSubscriptionActive = account.Active;
        user.AnlasBalance = account.AnlasBalance;
        user.NovelAiCheckedAt = DateTimeOffset.UtcNow;
    }

    private static NovelAiConnectionDto Describe(AppUser user, string? message = null) =>
        new(
            Connected: !string.IsNullOrEmpty(user.NovelAiTokenCipher),
            TokenLast4: user.NovelAiTokenLast4,
            Tier: user.NovelAiTier,
            SubscriptionActive: user.NovelAiSubscriptionActive,
            AnlasBalance: user.AnlasBalance,
            CheckedAt: user.NovelAiCheckedAt,
            Message: message
        );
}
