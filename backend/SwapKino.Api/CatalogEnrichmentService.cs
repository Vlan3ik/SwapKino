using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace SwapKino.Api;

/// <summary>Restart-safe detail backfill. The database state is the checkpoint.</summary>
public sealed class CatalogEnrichmentService(IServiceScopeFactory scopes,ILogger<CatalogEnrichmentService> log,IConfiguration config):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(!config.GetValue("CATALOG_DETAIL_BACKFILL_ENABLED",true))return;
        try{await Task.Delay(TimeSpan.FromSeconds(10),ct);}catch(OperationCanceledException){return;}
        while(!ct.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();var tmdb=scope.ServiceProvider.GetRequiredService<TmdbClient>();
                var batch=await db.Movies.AsNoTracking().Where(x=>x.DetailsState!="ready"&&x.DetailAttemptCount<5).OrderByDescending(x=>x.Popularity).ThenBy(x=>x.TmdbId).Take(20).Select(x=>new{x.TmdbId,x.IsSeries}).ToListAsync(ct);
                if(batch.Count==0){await Task.Delay(TimeSpan.FromMinutes(5),ct);continue;}
                foreach(var item in batch){try{await tmdb.Details(item.TmdbId,ct,item.IsSeries);}catch(Exception ex)when(ex is HttpRequestException or JsonException){log.LogWarning(ex,"Detail enrichment failed for {Type}/{Id}",item.IsSeries?"tv":"movie",item.TmdbId);}await Task.Delay(TimeSpan.FromMilliseconds(300),ct);}
                var runtimeNull=await db.Movies.CountAsync(x=>x.RuntimeMinutes==null,ct);var genresEmpty=await db.Movies.CountAsync(x=>!x.MovieGenres.Any(),ct);var failed=await db.Movies.CountAsync(x=>x.DetailsState=="failed",ct);
                log.LogInformation("Catalog detail metrics: runtime_null={RuntimeNull}, genres_empty={GenresEmpty}, details_failed={Failed}",runtimeNull,genresEmpty,failed);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            catch(Exception ex){log.LogWarning(ex,"Catalog detail backfill iteration failed");await Task.Delay(TimeSpan.FromSeconds(30),ct);}
        }
    }
}
