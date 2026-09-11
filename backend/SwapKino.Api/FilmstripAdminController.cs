using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "admin")]
public sealed class FilmstripAdminController(SwapKinoDbContext db, ProductRecommendationService recommendations, TmdbCardGateway cards) : ControllerBase
{
    [HttpGet("filmstrips")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Filmstrips.AsNoTracking().Include(x => x.Features).Include(x => x.References).OrderBy(x => x.CreatedAt).ToListAsync(ct));

    [HttpPost("filmstrips")]
    public async Task<IActionResult> Create(FilmstripRequest request, CancellationToken ct)
    {
        var error = Validate(request); if (error is not null) return BadRequest(new { message = error });
        var strip = ToEntity(request); strip.Slug = await UniqueSlug(strip.Slug, null, ct); db.Filmstrips.Add(strip); await db.SaveChangesAsync(ct);
        return Created($"/api/v1/admin/filmstrips/{strip.Id}", strip);
    }

    [HttpPatch("filmstrips/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, FilmstripRequest request, CancellationToken ct)
    {
        var error = Validate(request); if (error is not null) return BadRequest(new { message = error });
        var strip = await db.Filmstrips.Include(x => x.Features).Include(x => x.References).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (strip is null) return NotFound();
        var isSeries = request.IsSeries ?? false;
        strip.Name = request.Name.Trim(); strip.Slug = await UniqueSlug(Slug(strip.Name), id, ct); strip.IsSeries = isSeries; strip.ConfigVersion++;
        db.FilmstripFeatures.RemoveRange(strip.Features); db.FilmstripReferences.RemoveRange(strip.References);
        strip.Features = request.Features.Select(x => new FilmstripFeature { FilmstripId = id, FeatureType = x.FeatureType, TmdbFeatureId = x.TmdbFeatureId, Weight = x.Weight, Mode = x.Mode }).ToList();
        strip.References = request.References.Select(x => new FilmstripReference { FilmstripId = id, TmdbId = x.TmdbId, IsSeries = isSeries, Weight = x.Weight }).ToList();
        strip.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Ok(strip);
    }

    [HttpDelete("filmstrips/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var strip = await db.Filmstrips.FindAsync([id], ct);
        if (strip is null) return NotFound();
        db.Filmstrips.Remove(strip);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("filmstrips/{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => await SetStatus(id, "published", ct);

    [HttpPost("filmstrips/{id:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => await SetStatus(id, "draft", ct);

    [HttpPost("filmstrips/{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var strip = await db.Filmstrips.AsNoTracking().Include(x => x.References).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (strip is null) return NotFound();
        var preview = await recommendations.PreviewAsync(id, ct);
        var coverAvailable = false;
        if (strip.References.Count > 0)
        {
            try { var refs = strip.References.ToArray(); _ = await cards.GetAsync(new ProductDeckItem(refs[Random.Shared.Next(refs.Length)].TmdbId, strip.IsSeries), ct); coverAvailable = true; } catch (HttpRequestException) { coverAvailable = false; }
        }
        return Ok(new { preview.StrictCount, preview.RelaxedCount, preview.ReferenceRecommendations, preview.ReferenceSimilar, preview.UniqueCandidates, preview.DuplicateCount, coverAvailable, warning = preview.StrictCount < 20 || !coverAvailable ? "Строгая выдача мала или обложка недоступна" : null });
    }

    private async Task<IActionResult> SetStatus(Guid id, string status, CancellationToken ct)
    {
        var strip = await db.Filmstrips.FindAsync([id], ct);
        if (strip is null) return NotFound();
        strip.Status = status;
        strip.ConfigVersion++;
        strip.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(strip);
    }
    private static Filmstrip ToEntity(FilmstripRequest x) { var id = Guid.NewGuid(); var isSeries = x.IsSeries ?? false; return new Filmstrip { Id = id, Name = x.Name.Trim(), Slug = Slug(x.Name), IsSeries = isSeries, Features = x.Features.Select(f => new FilmstripFeature { FilmstripId = id, FeatureType = f.FeatureType, TmdbFeatureId = f.TmdbFeatureId, Weight = f.Weight, Mode = f.Mode }).ToList(), References = x.References.Select(r => new FilmstripReference { FilmstripId = id, TmdbId = r.TmdbId, IsSeries = isSeries, Weight = r.Weight }).ToList() }; }
    private async Task<string> UniqueSlug(string value, Guid? currentId, CancellationToken ct)
    {
        var baseSlug = Slug(value);
        if (baseSlug.Length == 0) baseSlug = "filmstrip";
        var candidate = baseSlug;
        var suffix = 2;
        while (await db.Filmstrips.AnyAsync(x => x.Slug == candidate && x.Id != currentId, ct))
            candidate = $"{baseSlug}-{suffix++}";
        return candidate;
    }
    private static string? Validate(FilmstripRequest x)
    {
        if (string.IsNullOrWhiteSpace(x.Name)) return "Название обязательно";
        return x.Features.Any(f => f.TmdbFeatureId <= 0) || x.References.Any(r => r.TmdbId <= 0)
            ? "Выберите корректные TMDB значения"
            : null;
    }
    private static string Slug(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9а-яё]+", "-").Trim('-');
}

internal sealed record SuggestionMovie(int Id, string Name, string? PosterPath, IReadOnlySet<int> GenreIds, string? OriginalLanguage);

public sealed record FilmstripRequest(string Name, bool? IsSeries, IReadOnlyList<FilmstripFeatureRequest> Features, IReadOnlyList<FilmstripReferenceRequest> References);
public sealed record FilmstripFeatureRequest(FilmstripFeatureType FeatureType, int TmdbFeatureId, double Weight, FilmstripFeatureMode Mode);
public sealed record FilmstripReferenceRequest(int TmdbId, double Weight);
