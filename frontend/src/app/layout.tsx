import type { Metadata } from "next";
import "./globals.css";
import { Toaster } from "@/components/ui/toaster";
import { AppShell } from "@/components/common/AppShell";

export const metadata: Metadata = {
  title: "СвайпКино — найди фильм на вечер",
  description:
    "Некоммерческий open-source сервис для подбора фильма на вечер в формате свайпов.",
  keywords: ["СвайпКино", "фильмы", "подбор", "свайпы", "кино"],
  authors: [{ name: "СвайпКино" }],
  icons: {
    icon: "/logo.svg",
  },
  openGraph: {
    title: "СвайпКино",
    description: "Найди фильм на вечер",
    siteName: "СвайпКино",
    type: "website",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru" className="dark" suppressHydrationWarning>
      <body className="antialiased bg-background text-foreground min-h-screen">
        <AppShell>{children}</AppShell>
        <Toaster />
      </body>
    </html>
  );
}
