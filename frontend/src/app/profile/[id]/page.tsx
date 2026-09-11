import { PublicProfileView } from "@/components/profile/PublicProfileView";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <PublicProfileView id={id} />;
}
