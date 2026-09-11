using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

[ApiController]
[Route("api/v1")]
public sealed class SocialController(SwapKinoDbContext db, ApiExternalServices external) : ControllerBase
{
    private const int PublicPageSize = 10;
    private TmdbCardGateway Cards => external.TmdbCards;
    private Guid? ViewerId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("users/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicProfile(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return NotFound(new { message = "Профиль не найден" });
        var states = db.UserMovieStates.AsNoTracking().Where(x => x.UserId == id);
        var ratingsCount = await states.CountAsync(x => x.Rating != null, ct);
        var watchedCount = await states.CountAsync(x => x.Watched, ct);
        var average = await states.Where(x => x.Rating != null).Select(x => x.Rating).AverageAsync(ct) ?? 0;
        var followers = await db.Follows.CountAsync(x => x.FollowingId == id, ct);
        var following = await db.Follows.CountAsync(x => x.FollowerId == id, ct);
        var viewer = ViewerId;
        var follows = viewer is not null && await db.Follows.AnyAsync(x => x.FollowerId == viewer && x.FollowingId == id, ct);
        var followedBy = viewer is not null && await db.Follows.AnyAsync(x => x.FollowerId == id && x.FollowingId == viewer, ct);
        var relation = follows && followedBy ? "friends" : follows ? "following" : followedBy ? "follower" : "none";
        var ratings = await StatePage(states.Where(x => x.Rating != null), ratingsCount, ct);
        var favoritesCount = await states.CountAsync(x => x.Favorite, ct);
        var favorites = await StatePage(states.Where(x => x.Favorite), favoritesCount, ct);
        var followingPage = await PeoplePage(db.Follows.Where(x => x.FollowerId == id), following, followers: false, ct: ct);
        var followersPage = await PeoplePage(db.Follows.Where(x => x.FollowingId == id), followers, followers: true, ct: ct);
        return Ok(new
        {
            user = PublicUser(user),
            statistics = new { ratingsCount, watchedCount, averageRating = Math.Round(average, 2), followersCount = followers, followingCount = following },
            relation,
            ratings = ratings.Items, ratingsPage = ratings.Page,
            favorites = favorites.Items, favoritesPage = favorites.Page,
            following = followingPage.Items, followingPage = followingPage.Page,
            followers = followersPage.Items, followersPage = followersPage.Page,
            comments = await TopComments(id, ct)
        });
    }

    [HttpGet("users/{id:guid}/ratings")]
    [AllowAnonymous]
    public Task<IActionResult> PublicRatings(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = PublicPageSize, CancellationToken ct = default) => PublicStateList(id, page, pageSize, favorite: false, ct: ct);

    [HttpGet("users/{id:guid}/favorites")]
    [AllowAnonymous]
    public Task<IActionResult> PublicFavorites(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = PublicPageSize, CancellationToken ct = default) => PublicStateList(id, page, pageSize, favorite: true, ct: ct);

    [HttpGet("users/{id:guid}/followers")]
    [AllowAnonymous]
    public Task<IActionResult> PublicFollowers(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = PublicPageSize, CancellationToken ct = default) => PublicPeopleList(id, page, pageSize, followers: true, ct);

    [HttpGet("users/{id:guid}/following")]
    [AllowAnonymous]
    public Task<IActionResult> PublicFollowing(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = PublicPageSize, CancellationToken ct = default) => PublicPeopleList(id, page, pageSize, followers: false, ct);

    [HttpPost("users/{id:guid}/follow")]
    [Authorize]
    public async Task<IActionResult> Follow(Guid id, CancellationToken ct)
    {
        var viewer = ViewerId!.Value;
        if (viewer == id) return BadRequest(new { message = "Нельзя подписаться на себя" });
        if (!await db.Users.AnyAsync(x => x.Id == id, ct)) return NotFound(new { message = "Профиль не найден" });
        if (!await db.Follows.AnyAsync(x => x.FollowerId == viewer && x.FollowingId == id, ct))
        {
            db.Follows.Add(new Follow { FollowerId = viewer, FollowingId = id });
            db.Notifications.Add(new Notification { RecipientId = id, ActorId = viewer, Type = "followed" });
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { relation = await Relation(viewer, id, ct) });
    }

    [HttpDelete("users/{id:guid}/follow")]
    [Authorize]
    public async Task<IActionResult> Unfollow(Guid id, CancellationToken ct)
    {
        var viewer = ViewerId!.Value;
        await db.Follows.Where(x => x.FollowerId == viewer && x.FollowingId == id).ExecuteDeleteAsync(ct);
        return Ok(new { relation = await Relation(viewer, id, ct) });
    }

    [HttpGet("movies/{id:int}/social")]
    [AllowAnonymous]
    public async Task<IActionResult> MovieSocial(int id, [FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        var viewer = ViewerId;
        if (viewer is null) return Ok(new { items = Array.Empty<object>() });
        var followedIds = db.Follows.Where(x => x.FollowerId == viewer).Select(x => x.FollowingId);
        var rows = await db.UserMovieStates.AsNoTracking().Where(x => followedIds.Contains(x.UserId) && x.TmdbId == id && x.IsSeries == isSeries && (x.Rating != null || x.Watched)).Join(db.Users.AsNoTracking(), state => state.UserId, user => user.Id, (state, user) => new { state, user }).OrderByDescending(x => x.state.Rating != null).ThenByDescending(x => x.state.UpdatedAt).Take(50).ToListAsync(ct);
        return Ok(new { items = rows.Select(x => new { user = PublicUser(x.user), rating = x.state.Rating, watched = x.state.Watched, updatedAt = x.state.UpdatedAt }).ToArray() });
    }

    [HttpGet("movies/{id:int}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> Comments(int id, [FromQuery] bool isSeries = false, [FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] Guid? focusCommentId = null, CancellationToken ct = default)
    {
        if (page < 1 || limit is < 1 or > 100) return ValidationProblem("Некорректные параметры страницы");
        var viewer = ViewerId;
        var comments = await db.MovieComments.AsNoTracking().Include(x => x.Author).Include(x => x.Reactions).Where(x => x.TmdbId == id && x.IsSeries == isSeries).ToListAsync(ct);
        var children = comments.Where(x => x.ParentCommentId is Guid).GroupBy(x => x.ParentCommentId!.Value).ToDictionary(x => x.Key, x => x.ToList());
        var roots = comments.Where(x => x.ParentCommentId is null)
            .OrderByDescending(x => x.AuthorId == viewer)
            .ThenByDescending(x => x.Reactions.Count(r => r.Type == "like") - x.Reactions.Count(r => r.Type == "dislike"))
            .ThenByDescending(x => x.CreatedAt).ToList();
        if (focusCommentId is Guid focus)
        {
            var target = comments.FirstOrDefault(x => x.Id == focus);
            while (target?.ParentCommentId is Guid parent) target = comments.FirstOrDefault(x => x.Id == parent);
            if (target is not null) page = Math.Max(1, roots.FindIndex(x => x.Id == target.Id) / limit + 1);
        }
        var selected = roots.Skip((page - 1) * limit).Take(limit).Select(x => CommentTree(x, children, viewer)).ToArray();
        return Ok(new { items = selected, page, pageSize = limit, totalCount = roots.Count, hasNextPage = page * limit < roots.Count });
    }

    [HttpPost("movies/{id:int}/comments")]
    [Authorize]
    public async Task<IActionResult> AddComment(int id, CommentRequest request, [FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return ValidationProblem("Комментарий должен содержать от 1 до 2000 символов");
        if (request.ParentCommentId is Guid parent && !await db.MovieComments.AnyAsync(x => x.Id == parent && x.TmdbId == id && x.IsSeries == isSeries, ct)) return NotFound(new { message = "Родительский комментарий не найден" });
        var comment = new MovieComment { AuthorId = ViewerId!.Value, TmdbId = id, IsSeries = isSeries, ParentCommentId = request.ParentCommentId, Text = text };
        db.MovieComments.Add(comment); await db.SaveChangesAsync(ct);
        if (request.ParentCommentId is Guid parentId)
        {
            var parentAuthor = await db.MovieComments.AsNoTracking().Where(x => x.Id == parentId).Select(x => x.AuthorId).FirstOrDefaultAsync(ct);
            if (parentAuthor != Guid.Empty && parentAuthor != ViewerId) { db.Notifications.Add(new Notification { RecipientId = parentAuthor, ActorId = ViewerId.Value, Type = "comment_replied", CommentId = comment.Id, TmdbId = id, IsSeries = isSeries }); await db.SaveChangesAsync(ct); }
        }
        return Created($"/api/v1/comments/{comment.Id}", new { id = comment.Id });
    }

    [HttpDelete("comments/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken ct)
    {
        var comment = await db.MovieComments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (comment is null) return NotFound();
        if (comment.AuthorId != ViewerId!.Value && !User.IsInRole("admin")) return Forbid();
        comment.Text = ""; comment.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("comments/{id:guid}/reaction")]
    [Authorize]
    public async Task<IActionResult> React(Guid id, ReactionRequest request, CancellationToken ct)
    {
        if (request.Type is not ("like" or "dislike")) return ValidationProblem("Допустимы только like и dislike");
        var comment = await db.MovieComments.FindAsync([id], ct); if (comment is null) return NotFound();
        var userId = ViewerId!.Value; var reaction = await db.CommentReactions.FindAsync([id, userId], ct); var shouldNotify = reaction is null || reaction.Type != request.Type;
        if (reaction?.Type == request.Type) db.CommentReactions.Remove(reaction);
        else if (reaction is null) db.CommentReactions.Add(new CommentReaction { CommentId = id, UserId = userId, Type = request.Type });
        else reaction.Type = request.Type;
        await db.SaveChangesAsync(ct);
        if (shouldNotify)
        {
            var authorId = await db.MovieComments.Where(x => x.Id == id).Select(x => x.AuthorId).FirstAsync(ct);
            if (authorId != userId) db.Notifications.Add(new Notification { RecipientId = authorId, ActorId = userId, Type = request.Type == "like" ? "comment_liked" : "comment_disliked", CommentId = id, TmdbId = comment.TmdbId, IsSeries = comment.IsSeries });
            await db.SaveChangesAsync(ct);
        }
        return Ok(await ReactionCounts(id, userId, ct));
    }

    [HttpDelete("comments/{id:guid}/reaction")]
    [Authorize]
    public async Task<IActionResult> RemoveReaction(Guid id, CancellationToken ct)
    {
        await db.CommentReactions.Where(x => x.CommentId == id && x.UserId == ViewerId).ExecuteDeleteAsync(ct);
        return Ok(await ReactionCounts(id, ViewerId!.Value, ct));
    }

    private async Task<IActionResult> PublicStateList(Guid id, int page, int pageSize, bool favorite, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 50) return ValidationProblem("Некорректные параметры страницы");
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound(new { message = "Профиль не найден" });
        var states = db.UserMovieStates.AsNoTracking().Where(x => x.UserId == id && (favorite ? x.Favorite : x.Rating != null));
        var total = await states.CountAsync(ct);
        var rows = await states.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items = await StateCards(rows, ct), page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), hasNextPage = page * pageSize < total });
    }

    private async Task<IActionResult> PublicPeopleList(Guid id, int page, int pageSize, bool followers, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 50) return ValidationProblem("Некорректные параметры страницы");
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound(new { message = "Профиль не найден" });
        var follows = db.Follows.AsNoTracking().Where(x => followers ? x.FollowingId == id : x.FollowerId == id);
        var total = await follows.CountAsync(ct);
        var ids = await follows.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => followers ? x.FollowerId : x.FollowingId).ToListAsync(ct);
        return Ok(new { items = await PublicUsers(ids, ct), page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), hasNextPage = page * pageSize < total });
    }

    private async Task<PagedResult> StatePage(IQueryable<UserMovieState> states, int total, CancellationToken ct)
    {
        var rows = await states.OrderByDescending(x => x.UpdatedAt).Take(PublicPageSize).ToListAsync(ct);
        return new PagedResult(await StateCards(rows, ct), PageInfo(1, PublicPageSize, total));
    }

    private async Task<PagedResult> PeoplePage(IQueryable<Follow> follows, int total, bool followers, CancellationToken ct)
    {
        var ids = await follows.OrderByDescending(x => x.CreatedAt).Take(PublicPageSize).Select(x => followers ? x.FollowerId : x.FollowingId).ToListAsync(ct);
        return new PagedResult(await PublicUsers(ids, ct), PageInfo(1, PublicPageSize, total));
    }

    private static object PageInfo(int page, int pageSize, int total) => new { page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), hasNextPage = page * pageSize < total };
    private static object CommentTree(MovieComment comment, IReadOnlyDictionary<Guid, List<MovieComment>> children, Guid? viewer)
    {
        var likes = comment.Reactions.Count(x => x.Type == "like"); var dislikes = comment.Reactions.Count(x => x.Type == "dislike");
        var replies = children.TryGetValue(comment.Id, out var rows) ? rows.OrderBy(x => x.CreatedAt).Select(x => CommentTree(x, children, viewer)).ToArray() : Array.Empty<object>();
        return new { id = comment.Id, parentCommentId = comment.ParentCommentId, text = comment.Text, comment.CreatedAt, comment.DeletedAt, comment.AuthorId, author = new { id = comment.Author.Id, name = comment.Author.DisplayName ?? "Пользователь", avatarUrl = comment.Author.AvatarUrl }, likes, dislikes, score = likes - dislikes, isMine = viewer == comment.AuthorId, myReaction = viewer is Guid user ? comment.Reactions.FirstOrDefault(x => x.UserId == user)?.Type : null, replies };
    }
    private async Task<object> ReactionCounts(Guid id, Guid userId, CancellationToken ct) => await db.CommentReactions.Where(x => x.CommentId == id).GroupBy(x => 1).Select(x => new { likes = x.Count(r => r.Type == "like"), dislikes = x.Count(r => r.Type == "dislike"), myReaction = x.Where(r => r.UserId == userId).Select(r => r.Type).FirstOrDefault() }).FirstOrDefaultAsync(ct) ?? new { likes = 0, dislikes = 0, myReaction = (string?)null };
    private async Task<string> Relation(Guid a, Guid b, CancellationToken ct) { var ab = await db.Follows.AnyAsync(x => x.FollowerId == a && x.FollowingId == b, ct); var ba = await db.Follows.AnyAsync(x => x.FollowerId == b && x.FollowingId == a, ct); return ab && ba ? "friends" : ab ? "following" : ba ? "follower" : "none"; }
    private static object PublicUser(User user) => new { id = user.Id, name = user.DisplayName ?? "Пользователь", avatarUrl = user.AvatarUrl };
    private async Task<object[]> PublicUsers(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return ids.Where(users.ContainsKey).Select(id => PublicUser(users[id])).ToArray();
    }
    private async Task<object[]> StateCards(IReadOnlyCollection<UserMovieState> rows, CancellationToken ct) => (await Task.WhenAll(rows.Select(async x => new { x.TmdbId, x.IsSeries, x.Rating, x.Watched, x.UpdatedAt, movie = TmdbCardGateway.Card(await Cards.GetAsync(new ProductDeckItem(x.TmdbId, x.IsSeries), ct), new ProductDeckItem(x.TmdbId, x.IsSeries)) }))).Cast<object>().ToArray();
    private async Task<object[]> TopComments(Guid authorId, CancellationToken ct)
    {
        var rows = await db.MovieComments.AsNoTracking().Include(x => x.Reactions)
            .Where(x => x.AuthorId == authorId && x.DeletedAt == null)
            .OrderByDescending(x => x.Reactions.Count(r => r.Type == "like") - x.Reactions.Count(r => r.Type == "dislike"))
            .ThenByDescending(x => x.CreatedAt).Take(10).ToListAsync(ct);
        return (await Task.WhenAll(rows.Select(async x =>
        {
            var item = new ProductDeckItem(x.TmdbId, x.IsSeries);
            return new
            {
                id = x.Id, text = x.Text, x.CreatedAt, x.TmdbId, x.IsSeries,
                likes = x.Reactions.Count(r => r.Type == "like"), dislikes = x.Reactions.Count(r => r.Type == "dislike"),
                score = x.Reactions.Count(r => r.Type == "like") - x.Reactions.Count(r => r.Type == "dislike"),
                movie = TmdbCardGateway.Card(await Cards.GetAsync(item, ct), item)
            };
        }))).Cast<object>().ToArray();
    }

    private sealed record PagedResult(object[] Items, object Page);
}

public sealed record CommentRequest(string? Text, Guid? ParentCommentId = null);
public sealed record ReactionRequest(string Type);
