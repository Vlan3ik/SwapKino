import { LicenseView } from "@/components/legal/LicenseView";
import type { Metadata } from "next";
export const metadata: Metadata = { title: "Лицензия", description: "Лицензионная информация проекта СвайпКино." };
export default function Page() { return <LicenseView />; }
