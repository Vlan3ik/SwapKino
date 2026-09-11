"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAppStore } from "@/lib/store";
import { ProfileView } from "@/components/profile/ProfileView";

export function ProfileRedirectView() {
  const router = useRouter();
  const user = useAppStore((state) => state.user);
  const hydrated = useAppStore((state) => state.hydrated);
  useEffect(() => { if (hydrated && user) router.replace(`/profile/${user.id}`); }, [hydrated, user, router]);
  if (!hydrated) return <div className="py-24 text-center text-muted-foreground">Открываем профиль…</div>;
  return <ProfileView />;
}
