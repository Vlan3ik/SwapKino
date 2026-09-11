function readBoolean(value: string | undefined, fallback: boolean) {
  if (value == null || value.trim() === "") return fallback;
  return value.toLowerCase() === "true" || value === "1";
}

export const featureFlags = {
  vibix: readBoolean(process.env.NEXT_PUBLIC_ENABLE_VIBIX, true),
  adBanner: readBoolean(process.env.NEXT_PUBLIC_ENABLE_AD_BANNER, true),
};
