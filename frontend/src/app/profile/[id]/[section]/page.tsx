import { PublicProfileListView } from "@/components/profile/PublicProfileListView";

export default async function Page({ params }: { params: Promise<{ id: string; section: string }> }) {
  const { id, section } = await params;
  return <PublicProfileListView id={id} section={section} />;
}
