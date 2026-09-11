import { CopyrightView } from "@/components/legal/CopyrightView";
import type { Metadata } from "next";
export const metadata: Metadata = { title: "Авторские права", description: "Информация об авторских правах и источниках данных СвайпКино." };
export default function Page() { return <CopyrightView />; }
