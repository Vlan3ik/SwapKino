"use client";

import { useEffect, useState } from "react";
import {
  ArrowLeft,
  Eye,
  Film,
  Loader2,
  Plus,
  Save,
  Search,
  Shield,
  Trash2,
  X,
} from "lucide-react";
import { useRouter } from "next/navigation";
import {
  api,
  type AdminFeature,
  type AdminFilmstrip,
  type AdminReference,
} from "@/lib/api";
import { useAppStore } from "@/lib/store";

type Option = { id: number; name: string; poster_path?: string | null };
type FeatureRow = AdminFeature & { label?: string };
type RefRow = AdminReference & { title?: string; posterPath?: string | null };
type Draft = {
  name: string;
  isSeries: boolean;
  features: FeatureRow[];
  references: RefRow[];
};
const blank = (): Draft => ({
  name: "",
  isSeries: false,
  features: [],
  references: [],
});

export function AdminView() {
  const router = useRouter();
  const user = useAppStore((s) => s.user);
  const isAdmin = Boolean(user?.roles.includes("admin"));
  const [items, setItems] = useState<AdminFilmstrip[]>([]);
  const [selected, setSelected] = useState<AdminFilmstrip | null>(null);
  const [draft, setDraft] = useState<Draft>(blank());
  const [keywords, setKeywords] = useState<Option[]>([]);
  const [keywordQuery, setKeywordQuery] = useState("");
  const [movieResults, setMovieResults] = useState<Option[]>([]);
  const [movieQuery, setMovieQuery] = useState("");
  const [suggestions, setSuggestions] = useState<{
    movies: Option[];
    genres: Option[];
    keywords: Option[];
  }>({ movies: [], genres: [], keywords: [] });
  const [editorOpen, setEditorOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [preview, setPreview] = useState<any>(null);

  useEffect(() => {
    if (!user) {
      router.replace("/profile");
      return;
    }
    if (!isAdmin) {
      setLoading(false);
      return;
    }
    void api
      .adminFilmstrips()
      .then((films) => {
        setItems(films);
      })
      .catch((e) =>
        setMessage(
          e instanceof Error ? e.message : "Не удалось загрузить админку",
        ),
      )
      .finally(() => setLoading(false));
  }, [isAdmin, router, user]);

  useEffect(() => {
    if (keywordQuery.trim().length < 2) {
      setKeywords([]);
      return;
    }
    const timer = window.setTimeout(
      () =>
        void api
          .adminKeywords(keywordQuery)
          .then((data) =>
            setKeywords(
              (data.results ?? []).map((x) => ({ id: x.id, name: x.name })),
            ),
          )
          .catch(() => undefined),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [keywordQuery]);

  useEffect(() => {
    if (movieQuery.trim().length < 2) {
      setMovieResults([]);
      return;
    }
    const timer = window.setTimeout(
      () =>
        void api
          .adminSearchMovies(movieQuery, draft.isSeries)
          .then((data) =>
            setMovieResults(
              (data.results ?? [])
                .map((x) => ({
                  id: x.id,
                  name: x.title ?? x.name ?? "",
                  poster_path: x.poster_path,
                }))
                .slice(0, 8),
            ),
          )
          .catch(() => undefined),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [draft.isSeries, movieQuery]);

  useEffect(() => {
    if (draft.name.trim().length < 2 && draft.references.length === 0) {
      setSuggestions({ movies: [], genres: [], keywords: [] });
      return;
    }
    const timer = window.setTimeout(
      () =>
        void api
          .adminSuggestions(
            draft.name,
            draft.references.map((x) => x.tmdbId),
            draft.features.map((x) => x.tmdbFeatureId),
            draft.features
              .filter((x) => x.featureType === "genre")
              .map((x) => x.tmdbFeatureId),
            draft.isSeries,
          )
          .then((data) =>
            setSuggestions({
              movies: (data.movies ?? []).map((x) => ({
                id: x.id,
                name: x.name,
                poster_path: x.poster_path,
              })),
              genres: (data.genres ?? []).map((x) => ({
                id: x.id,
                name: x.name,
              })),
              keywords: (data.keywords ?? []).map((x) => ({
                id: x.id,
                name: x.name,
              })),
            }),
          )
          .catch(() => undefined),
      500,
    );
    return () => window.clearTimeout(timer);
  }, [draft.name, draft.isSeries, draft.references, draft.features]);

  const open = (item: AdminFilmstrip | null) => {
    setSelected(item);
    setEditorOpen(true);
    setPreview(null);
    setMessage(null);
    setDraft(
      item
        ? {
            name: item.name,
            isSeries: item.isSeries,
            features: item.features ?? [],
            references: item.references ?? [],
          }
        : blank(),
    );
  };
  const payload = () => ({
    name: draft.name.trim(),
    isSeries: draft.isSeries,
    features: draft.features.map(({ label, ...x }) => x),
    references: draft.references.map(({ title, posterPath, ...x }) => x),
  });
  const save = async () => {
    if (!draft.name.trim() || draft.references.length === 0) {
      setMessage("Добавьте название и хотя бы один фильм-референс");
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      const saved = selected?.id
        ? await api.adminUpdateFilmstrip(selected.id, payload())
        : await api.adminCreateFilmstrip(payload());
      setItems((all) =>
        selected?.id
          ? all.map((x) => (x.id === saved.id ? saved : x))
          : [...all, saved],
      );
      setSelected(saved);
      setDraft({
        name: saved.name,
        isSeries: saved.isSeries,
        features: saved.features ?? [],
        references: saved.references ?? [],
      });
      setMessage("Сохранено");
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Ошибка сохранения");
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    if (!selected?.id || !confirm("Удалить киноплёнку?")) return;
    setBusy(true);
    try {
      await api.adminDeleteFilmstrip(selected.id);
      setItems((x) => x.filter((v) => v.id !== selected.id));
      setSelected(null);
      setEditorOpen(false);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Ошибка удаления");
    } finally {
      setBusy(false);
    }
  };
  const changeStatus = async (status: "publish" | "unpublish") => {
    if (!selected?.id) return;
    setBusy(true);
    try {
      const saved = await api.adminSetFilmstripStatus(selected.id, status);
      setItems((x) => x.map((v) => (v.id === saved.id ? saved : v)));
      setSelected(saved);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Ошибка публикации");
    } finally {
      setBusy(false);
    }
  };

  if (!isAdmin)
    return (
      <div className="mx-auto max-w-3xl px-4 py-20 text-center">
        <Shield className="mx-auto h-12 w-12 text-rating" />
        <h1 className="mt-4 text-2xl font-bold">
          Доступ только для администратора
        </h1>
      </div>
    );
  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <button
            type="button"
            onClick={() => router.push("/profile")}
            className="mb-3 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-white"
          >
            <ArrowLeft className="h-4 w-4" /> Профиль
          </button>
          <h1 className="text-3xl font-bold">Киноплёнки</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Подборки из русифицированных TMDB-тегов и фильмов-референсов.
          </p>
        </div>
        <button
          type="button"
          onClick={() => open(null)}
          className="inline-flex items-center gap-2 rounded-xl bg-rating px-4 py-2.5 font-bold text-black"
        >
          <Plus className="h-4 w-4" /> Новая киноплёнка
        </button>
      </div>
      {message && (
        <div className="mb-4 rounded-xl border border-rating/30 bg-rating/10 px-4 py-3 text-sm">
          {message}
        </div>
      )}
      <div className="grid gap-5 lg:grid-cols-[300px_1fr]">
        <aside className="glass-panel rounded-2xl p-3">
          <div className="mb-2 px-2 text-xs uppercase tracking-wider text-muted-foreground">
            Добавленные · {items.length}
          </div>
          {loading ? (
            <Loader2 className="m-5 h-5 animate-spin text-rating" />
          ) : (
            items.map((item) => (
              <button
                type="button"
                key={item.id}
                onClick={() => open(item)}
                className={`mb-1 w-full rounded-xl p-3 text-left ${selected?.id === item.id ? "bg-rating/15 ring-1 ring-rating/40" : "hover:bg-white/5"}`}
              >
                <div className="flex justify-between gap-2">
                  <b>{item.name}</b>
                  <span
                    className={`text-[10px] uppercase ${item.status === "published" ? "text-like" : "text-muted-foreground"}`}
                  >
                    {item.status === "published" ? "live" : "draft"}
                  </span>
                </div>
                <span className="text-xs text-muted-foreground">
                  {item.isSeries ? "Сериалы" : "Фильмы"} ·{" "}
                  {item.references?.length ?? 0} референсов
                </span>
              </button>
            ))
          )}
        </aside>
        <section className="glass-panel rounded-2xl p-5 sm:p-7">
          {editorOpen ? (
            <Editor
              draft={draft}
              setDraft={setDraft}
              keywords={keywords}
              keywordQuery={keywordQuery}
              setKeywordQuery={setKeywordQuery}
              movieQuery={movieQuery}
              setMovieQuery={setMovieQuery}
              movieResults={movieResults}
              suggestions={suggestions}
              busy={busy}
              selected={selected}
              onSave={save}
              onDelete={remove}
              onStatus={changeStatus}
              preview={preview}
              setPreview={setPreview}
            />
          ) : (
            <div className="flex min-h-[430px] flex-col items-center justify-center text-center">
              <Film className="h-12 w-12 text-rating" />
              <h2 className="mt-4 text-xl font-bold">Выберите киноплёнку</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                Или создайте новую подборку.
              </p>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

function Editor({
  draft,
  setDraft,
  keywords,
  keywordQuery,
  setKeywordQuery,
  movieQuery,
  setMovieQuery,
  movieResults,
  suggestions,
  busy,
  selected,
  onSave,
  onDelete,
  onStatus,
  preview,
  setPreview,
}: any) {
  const genres: Option[] = [];
  const addFeature = (option: Option, type: "genre" | "keyword") => {
    if (
      draft.features.some(
        (x: FeatureRow) =>
          x.featureType === type && x.tmdbFeatureId === option.id,
      )
    )
      return;
    setDraft({
      ...draft,
      features: [
        ...draft.features,
        {
          featureType: type,
          tmdbFeatureId: option.id,
          weight: 1,
          mode: "preferred",
          label: option.name,
        },
      ],
    });
  };
  const addRef = (option: Option) => {
    if (draft.references.some((x: RefRow) => x.tmdbId === option.id)) return;
    setDraft({
      ...draft,
      references: [
        ...draft.references,
        {
          tmdbId: option.id,
          weight: 1,
          title: option.name,
          posterPath: option.poster_path,
        },
      ],
    });
    setMovieQuery("");
  };
  const runPreview = async () => {
    if (!selected?.id) return;
    setPreview(await api.adminPreviewFilmstrip(selected.id));
  };
  const selectedFeatureIds = new Set(
    draft.features.map((x: FeatureRow) => x.tmdbFeatureId),
  );
  const suggestedGenres = suggestions.genres.filter(
    (x: Option, i: number, all: Option[]) =>
      !selectedFeatureIds.has(x.id) &&
      all.findIndex((v) => v.name === x.name) === i,
  );
  const suggestedKeywords = suggestions.keywords.filter(
    (x: Option, i: number, all: Option[]) =>
      !selectedFeatureIds.has(x.id) &&
      all.findIndex((v) => v.name === x.name) === i,
  );
  return (
    <div>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-bold">
            {selected ? "Редактирование" : "Новая киноплёнка"}
          </h2>
          {selected && (
            <p className="text-xs text-muted-foreground">
              Slug генерируется автоматически: /{selected.slug}
            </p>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          {selected?.id && (
            <>
              <button
                type="button"
                onClick={runPreview}
                disabled={busy}
                className="inline-flex items-center gap-1 rounded-lg border border-white/10 px-3 py-2 text-sm"
              >
                <Eye className="h-4 w-4" /> Preview
              </button>
              <button
                type="button"
                onClick={() =>
                  onStatus(
                    selected.status === "published" ? "unpublish" : "publish",
                  )
                }
                disabled={busy}
                className="rounded-lg border border-like/30 px-3 py-2 text-sm text-like"
              >
                {selected.status === "published" ? "Снять" : "Опубликовать"}
              </button>
              <button
                type="button"
                onClick={onDelete}
                disabled={busy}
                className="rounded-lg border border-red-400/20 p-2 text-red-300"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </>
          )}
          <button
            type="button"
            onClick={onSave}
            disabled={busy}
            className="inline-flex items-center gap-1 rounded-lg bg-white px-3 py-2 text-sm font-bold text-black"
          >
            <Save className="h-4 w-4" /> Сохранить
          </button>
        </div>
      </div>
      <label className="block text-sm">
        <span className="mb-1 block text-xs text-muted-foreground">
          Название
        </span>
        <input
          className="admin-input"
          value={draft.name}
          onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          placeholder="Например, Тёмные расследования"
        />
      </label>
      <label className="mt-4 flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={draft.isSeries}
          onChange={(e) =>
            setDraft({ ...draft, isSeries: e.target.checked, references: [] })
          }
        />{" "}
        Подборка сериалов
      </label>
      <div className="mt-8">
        <h3 className="mb-3 font-semibold">Теги</h3>
        <div className="flex flex-wrap gap-2">
          {genres.map((x: Option) => (
            <button
              type="button"
              key={x.id}
              onClick={() => addFeature(x, "genre")}
              className="rounded-full border border-white/10 px-3 py-1.5 text-xs hover:border-rating/50"
            >
              {x.name}
            </button>
          ))}
        </div>
        <div className="mt-3 flex gap-2">
          <input
            className="admin-input"
            value={keywordQuery}
            onChange={(e) => setKeywordQuery(e.target.value)}
            placeholder="Найти keyword…"
          />
          <Search className="mt-2 h-5 w-5 text-muted-foreground" />
        </div>
        <div className="mt-2 flex flex-wrap gap-2">
          {keywords.map((x: Option) => (
            <button
              type="button"
              key={x.id}
              onClick={() => addFeature(x, "keyword")}
              className="rounded-full bg-rating/10 px-3 py-1.5 text-xs text-rating"
            >
              {x.name}
            </button>
          ))}
        </div>
        {(suggestedGenres.length > 0 || suggestedKeywords.length > 0) && (
          <div className="mt-4 rounded-xl border border-rating/20 bg-rating/5 p-3">
            <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-rating">
              Подсказки по тегам
            </div>
            <div className="flex flex-wrap gap-2">
              {suggestedGenres.map((x: Option) => (
                <button
                  type="button"
                  key={`suggested-genre-${x.id}`}
                  onClick={() => addFeature(x, "genre")}
                  className="rounded-full border border-rating/30 px-3 py-1.5 text-xs"
                >
                  + {x.name}
                </button>
              ))}
              {suggestedKeywords.map((x: Option) => (
                <button
                  type="button"
                  key={`suggested-keyword-${x.id}`}
                  onClick={() => addFeature(x, "keyword")}
                  className="rounded-full border border-rating/30 px-3 py-1.5 text-xs"
                >
                  + {x.name}
                </button>
              ))}
            </div>
          </div>
        )}
        <div className="mt-4 space-y-2">
          {draft.features.map((x: FeatureRow, i: number) => (
            <div
              key={`${x.featureType}-${x.tmdbFeatureId}`}
              className="flex flex-wrap items-center gap-2 rounded-xl border border-white/10 p-2"
            >
              <span className="rounded bg-white/10 px-2 py-1 text-xs">
                {x.featureType === "genre" ? "Жанр" : "Keyword"}
              </span>
              <b className="text-sm">
                {x.label ?? `${x.featureType} #${x.tmdbFeatureId}`}
              </b>
              <select
                className="admin-input !w-auto"
                value={x.mode}
                onChange={(e) => {
                  const features = [...draft.features];
                  features[i] = { ...x, mode: e.target.value };
                  setDraft({ ...draft, features });
                }}
              >
                <option value="preferred">желательный</option>
                <option value="required">обязательный</option>
                <option value="excluded">исключить</option>
              </select>
              <button
                type="button"
                onClick={() =>
                  setDraft({
                    ...draft,
                    features: draft.features.filter(
                      (_: any, n: number) => n !== i,
                    ),
                  })
                }
                className="ml-auto text-muted-foreground"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          ))}
        </div>
      </div>
      <div className="mt-8">
        <h3 className="mb-1 font-semibold">Фильмы-референсы</h3>
        <p className="mb-3 text-xs text-muted-foreground">
          Подборка строится вокруг этих фильмов. Обложка каждый раз выбирается
          случайно из списка.
        </p>
        <div className="flex gap-2">
          <input
            className="admin-input"
            value={movieQuery}
            onChange={(e) => setMovieQuery(e.target.value)}
            placeholder="Найти фильм в TMDB…"
          />
          <Search className="mt-2 h-5 w-5 text-muted-foreground" />
        </div>
        {movieResults.length > 0 && (
          <div className="mt-2 grid gap-2 sm:grid-cols-2">
            {movieResults.map((x: Option) => (
              <button
                type="button"
                key={x.id}
                onClick={() => addRef(x)}
                className="flex items-center gap-3 rounded-xl border border-white/10 p-2 text-left hover:border-rating/50"
              >
                {x.poster_path ? (
                  <img
                    src={`https://image.tmdb.org/t/p/w92${x.poster_path}`}
                    className="h-12 w-8 rounded object-cover"
                    alt=""
                  />
                ) : (
                  <div className="h-12 w-8 rounded bg-white/10" />
                )}
                <span className="text-sm font-semibold">{x.name}</span>
                <Plus className="ml-auto h-4 w-4 text-rating" />
              </button>
            ))}
          </div>
        )}
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          {draft.references.map((x: RefRow, i: number) => (
            <div
              key={x.tmdbId}
              className="flex items-center gap-3 rounded-xl border border-white/10 p-2"
            >
              {x.posterPath ? (
                <img
                  src={`https://image.tmdb.org/t/p/w92${x.posterPath}`}
                  className="h-14 w-9 rounded object-cover"
                  alt=""
                />
              ) : (
                <div className="h-14 w-9 rounded bg-white/10" />
              )}
              <span className="min-w-0 flex-1 truncate text-sm font-semibold">
                {x.title ?? "Фильм из TMDB"}
              </span>
              <button
                type="button"
                onClick={() =>
                  setDraft({
                    ...draft,
                    references: draft.references.filter(
                      (_: any, n: number) => n !== i,
                    ),
                  })
                }
                className="text-muted-foreground hover:text-red-300"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          ))}
        </div>
        {suggestions.movies.length > 0 && (
          <div className="mt-5 rounded-xl border border-rating/20 bg-rating/5 p-3">
            <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-rating">
              Похожие фильмы по вашим референсам
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
              {suggestions.movies
                .filter(
                  (x) =>
                    !draft.references.some((r: RefRow) => r.tmdbId === x.id),
                )
                .slice(0, 8)
                .map((x) => (
                  <button
                    type="button"
                    key={`suggested-movie-${x.id}`}
                    onClick={() => addRef(x)}
                    className="flex items-center gap-3 rounded-xl border border-white/10 p-2 text-left hover:border-rating/50"
                  >
                    {x.poster_path ? (
                      <img
                        src={`https://image.tmdb.org/t/p/w92${x.poster_path}`}
                        className="h-12 w-8 rounded object-cover"
                        alt=""
                      />
                    ) : (
                      <div className="h-12 w-8 rounded bg-white/10" />
                    )}
                    <span className="text-sm font-semibold">{x.name}</span>
                    <Plus className="ml-auto h-4 w-4 text-rating" />
                  </button>
                ))}
            </div>
          </div>
        )}
      </div>
      {preview && (
        <div className="mt-8 rounded-xl border border-rating/20 bg-rating/5 p-4 text-sm">
          <b>Preview</b>
          <div className="mt-2 grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Metric label="Strict" value={preview.strictCount} />
            <Metric label="Relaxed" value={preview.relaxedCount} />
            <Metric label="Уникальных" value={preview.uniqueCandidates} />
            <Metric label="Дублей" value={preview.duplicateCount} />
          </div>
        </div>
      )}
    </div>
  );
}
function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <span className="text-xs text-muted-foreground">{label}</span>
      <div className="text-lg font-bold">{value}</div>
    </div>
  );
}
