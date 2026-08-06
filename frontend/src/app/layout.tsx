import type { Metadata } from "next";
import { Geist } from "next/font/google";
import "./globals.css";
import { Toaster } from "@/components/ui/toaster";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin", "cyrillic"],
});

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
      <body
        className={`${geistSans.variable} antialiased bg-background text-foreground min-h-screen`}
      >
        {children}
        <Toaster />
      </body>
    </html>
  );
}
