import { TermsView } from "@/components/legal/TermsView";
import type { Metadata } from "next";
export const metadata: Metadata = { title: "Условия использования", description: "Условия использования сервиса СвайпКино." };
export default function Page() { return <TermsView />; }
