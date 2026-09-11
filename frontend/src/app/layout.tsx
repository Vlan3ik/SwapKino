import type { Metadata } from "next";
import "./globals.css";
import { Toaster } from "@/components/ui/toaster";
import { AppShell } from "@/components/common/AppShell";
import Script from "next/script";
import { featureFlags } from "@/lib/featureFlags";
import { PlausibleAnalytics } from "@/components/common/PlausibleAnalytics";
import { SITE_NAME, SITE_URL } from "@/lib/site";

export const metadata: Metadata = {
  metadataBase: new URL(`${SITE_URL}/`),
  title: { default: `${SITE_NAME} — найди фильм на вечер`, template: `%s — ${SITE_NAME}` },
  description:
    "Некоммерческий open-source сервис для подбора фильма на вечер в формате свайпов.",
  keywords: ["СвайпКино", "фильмы", "подбор", "свайпы", "кино"],
  authors: [{ name: "СвайпКино" }],
  icons: {
    icon: [
      { url: "/favicon-32x32.png", sizes: "32x32", type: "image/png" },
      { url: "/icon.svg", type: "image/svg+xml" },
      { url: "/favicon.ico" },
    ],
    apple: "/app-icons/apple-touch-icon.png",
  },
  openGraph: {
    title: "СвайпКино",
    description: "Найди фильм на вечер",
    siteName: "СвайпКино",
    type: "website",
    locale: "ru_RU",
    url: SITE_URL,
  },
  twitter: { card: "summary_large_image", title: SITE_NAME, description: "Найди фильм на вечер" },
  alternates: { canonical: SITE_URL },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru" className="dark" suppressHydrationWarning>
      <head>
        {featureFlags.vibix && <Script src="https://graphicslab.io/sdk/v2/rendex-sdk.min.js" strategy="beforeInteractive" />}
        {featureFlags.adBanner && <Script src="https://v-js-menu.run/public/lib.en.min.js" strategy="afterInteractive" />}
      </head>
      <body className="antialiased bg-background text-foreground min-h-screen">
        <AppShell>{children}</AppShell>
        <PlausibleAnalytics />
        {featureFlags.adBanner && <ins id="vibix_union" data-publisher_id="678712186" data-add_types="banners" />}
        <Toaster />
      </body>
    </html>
  );
}
