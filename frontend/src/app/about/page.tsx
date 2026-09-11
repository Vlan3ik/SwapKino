import { AboutView } from "@/components/legal/AboutView";
import type { Metadata } from "next";
export const metadata: Metadata = { title: "О проекте", description: "Открытый проект СвайпКино для поиска фильмов и сериалов." };
export default function Page() { return <AboutView />; }
