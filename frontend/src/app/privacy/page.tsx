import { PrivacyView } from "@/components/legal/PrivacyView";
import type { Metadata } from "next";
export const metadata: Metadata = { title: "Политика конфиденциальности", description: "Политика обработки данных и конфиденциальности СвайпКино." };
export default function Page() { return <PrivacyView />; }
