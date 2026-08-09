const imageCache = new Map<string, Promise<HTMLImageElement>>();

export function preloadImage(src: string | null | undefined): Promise<HTMLImageElement | null> {
  if (!src) return Promise.resolve(null);
  const cached = imageCache.get(src);
  if (cached) return cached;

  const promise = new Promise<HTMLImageElement>((resolve) => {
    const image = new Image();
    image.decoding = "async";
    image.loading = "eager";
    image.onload = () => { void image.decode().catch(() => undefined).finally(() => resolve(image)); };
    image.onerror = () => resolve(image);
    image.src = src;
  });
  imageCache.set(src, promise);
  return promise;
}

export function preloadImages(sources: Array<string | null | undefined>): Promise<Array<HTMLImageElement | null>> {
  return Promise.all([...new Set(sources.filter((source): source is string => Boolean(source)))].map(preloadImage));
}
