using ComicStudio.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ComicStudio.Api.Hubs;

public static class GenerationEvents
{
    public const string Started = "generation.started";
    public const string Token = "generation.token";
    public const string Completed = "generation.completed";
    public const string Failed = "generation.failed";
}

public static class ImageEvents
{
    public const string Started = "image.started";
    public const string Completed = "image.completed";
    public const string Failed = "image.failed";
    public const string CurationProgress = "curation.progress";
}

/// <summary>
/// Pushes generation progress to the owning user only. Groups are keyed by user
/// id so a client cannot subscribe to another account's stream.
/// </summary>
[Authorize]
public class GenerationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(userId));
        }

        await base.OnConnectedAsync();
    }

    public static string GroupFor(string userId) => $"user:{userId}";
}
