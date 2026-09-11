using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController(SwapKinoDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 30, CancellationToken ct = default)
    {
        if (limit is < 1 or > 100) return ValidationProblem("Limit должен быть от 1 до 100");
        var rows = await db.Notifications.AsNoTracking().Where(x => x.RecipientId == UserId).OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new
        {
            x.Id, x.Type, x.CommentId, x.TmdbId, x.IsSeries, x.ReadAt, x.CreatedAt,
            actor = new { id = x.Actor.Id, name = x.Actor.DisplayName ?? "Пользователь", avatarUrl = x.Actor.AvatarUrl }
        }).ToListAsync(ct);
        var unreadCount = await db.Notifications.CountAsync(x => x.RecipientId == UserId && x.ReadAt == null, ct);
        return Ok(new { items = rows, unreadCount });
    }

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(CancellationToken ct)
    {
        await db.Notifications.Where(x => x.RecipientId == UserId && x.ReadAt == null).ExecuteUpdateAsync(x => x.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await db.Notifications.Where(x => x.Id == id && x.RecipientId == UserId).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
