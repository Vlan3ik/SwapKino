import { ImportProgress } from "@/components/imports/KinopoiskImport";
export default async function Page({ params }: { params: Promise<{ jobId: string }> }) { const { jobId } = await params; return <ImportProgress jobId={jobId}/>; }
