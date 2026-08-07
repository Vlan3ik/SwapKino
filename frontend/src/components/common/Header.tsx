"use client";

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  LayoutGrid,
  Heart,
  User,
  ChevronDown,
  Flame,
  LogOut,
  Star,
} from "lucide-react";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import { AuthModal } from "./AuthModal";

export function Header() {
  const { view, setView, favorites, user, logout, ratings } = useAppStore();
  const [profileOpen, setProfileOpen] = useState(false);
  const [authOpen, setAuthOpen] = useState(false);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");

  const activeName = view.name;
  const ratedCount = Object.keys(ratings).length;

  const openAuth = (mode: "login" | "register") => {
    setAuthMode(mode);
    setAuthOpen(true);
    setProfileOpen(false);
  };

  return (
    <>
      <header className="sticky top-0 z-50 glass-panel-strong border-b border-white/5">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="flex h-16 items-center justify-between gap-4">
            {/* Лого */}
            <a
              href="/"
              onClick={() => setView({ name: "feed" })}
              className="flex items-center gap-2.5 group no-select"
            >
              <motion.div
                whileHover={{ rotate: -8, scale: 1.05 }}
                transition={{ type: "spring", stiffness: 300, damping: 15 }}
                className="relative"
              >
                <div className="absolute inset-0 bg-rating/30 blur-lg rounded-full" />
                <Flame
                  className="relative h-7 w-7 text-rating"
                  fill="currentColor"
                />
              </motion.div>
              <div className="flex flex-col items-start leading-none">
                <span className="text-lg font-bold tracking-tight text-foreground">
                  СвайпКино
                </span>
              </div>
            </a>

            {/* Навигация по центру */}
            <nav className="hidden md:flex items-center gap-1">
              <NavButton
                icon={<Flame className="h-4 w-4" />}
                label="Подборки"
                href="/"
                active={activeName === "feed"}
                onClick={() => setView({ name: "feed" })}
              />
              <NavButton
                icon={<LayoutGrid className="h-4 w-4" />}
                label="Каталог"
                href="/catalog"
                active={activeName === "catalog"}
                onClick={() => setView({ name: "catalog" })}
              />
            </nav>

            {/* Профиль / Авторизация */}
            {user ? (
              <div
                className="relative"
                onMouseEnter={() => setProfileOpen(true)}
                onMouseLeave={() => setProfileOpen(false)}
              >
                <button
                  onClick={() => setProfileOpen((v) => !v)}
                  className={cn(
                    "flex items-center gap-2 rounded-full pl-1.5 pr-3 py-1.5 transition-all",
                    "border border-white/10 hover:border-white/20",
                    profileOpen ||
                      activeName === "profile" ||
                      activeName === "favorites"
                      ? "bg-white/10"
                      : "bg-transparent"
                  )}
                >
                  <div className="h-7 w-7 rounded-full bg-gradient-to-br from-rating/80 to-skip/60 flex items-center justify-center border border-white/10">
                    <span className="text-xs font-bold text-black">
                      {user.username[0]?.toUpperCase()}
                    </span>
                  </div>
                  <span className="text-sm font-medium hidden sm:inline max-w-[120px] truncate">
                    {user.username}
                  </span>
                  <ChevronDown
                    className={cn(
                      "h-3.5 w-3.5 text-muted-foreground transition-transform",
                      profileOpen && "rotate-180"
                    )}
                  />
                </button>

                <AnimatePresence>
                  {profileOpen && (
                    <motion.div
                      initial={{ opacity: 0, y: 8, scale: 0.96 }}
                      animate={{ opacity: 1, y: 0, scale: 1 }}
                      exit={{ opacity: 0, y: 8, scale: 0.96 }}
                      transition={{ duration: 0.18 }}
                      className="absolute right-0 top-full mt-2 w-60 glass-panel-strong rounded-xl p-1.5 shadow-cinematic"
                    >
                      <div className="px-3 py-2.5 border-b border-white/5 mb-1">
                        <p className="text-sm font-semibold truncate">
                          {user.username}
                        </p>
                        <p className="text-xs text-muted-foreground truncate">
                          {user.email}
                        </p>
                      </div>
                      <DropdownItem
                        icon={<User className="h-4 w-4" />}
                        label="Профиль"
                        onClick={() => {
                          setView({ name: "profile" });
                          setProfileOpen(false);
                        }}
                      />
                      <div className="h-px bg-white/5 my-1" />
                      <DropdownItem
                        icon={<LogOut className="h-4 w-4" />}
                        label="Выйти"
                        danger
                        onClick={() => {
                          logout();
                          setProfileOpen(false);
                        }}
                      />
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            ) : (
              <div className="flex items-center gap-1.5">
                {/* Иконки быстрых разделов для гостя */}
                <button
                  onClick={() => setView({ name: "favorites" })}
                  aria-label="Избранное"
                  className={cn(
                    "relative h-9 w-9 rounded-full border transition-all flex items-center justify-center",
                    activeName === "favorites"
                      ? "border-like/40 bg-like/10 text-like"
                      : "border-white/10 hover:bg-white/5 text-muted-foreground hover:text-foreground"
                  )}
                >
                  <Heart className="h-4 w-4" />
                  {favorites.length > 0 && (
                    <span className="absolute -top-1 -right-1 bg-like text-black text-[9px] font-bold px-1 rounded-full min-w-[15px] h-[15px] flex items-center justify-center">
                      {favorites.length}
                    </span>
                  )}
                </button>
                <button
                  onClick={() => setView({ name: "ratings" })}
                  aria-label="Мои оценки"
                  className={cn(
                    "relative h-9 w-9 rounded-full border transition-all flex items-center justify-center",
                    activeName === "ratings"
                      ? "border-rating/40 bg-rating/10 text-rating"
                      : "border-white/10 hover:bg-white/5 text-muted-foreground hover:text-foreground"
                  )}
                >
                  <Star className="h-4 w-4" />
                  {ratedCount > 0 && (
                    <span className="absolute -top-1 -right-1 bg-rating text-black text-[9px] font-bold px-1 rounded-full min-w-[15px] h-[15px] flex items-center justify-center">
                      {ratedCount}
                    </span>
                  )}
                </button>
                <button
                  onClick={() => openAuth("login")}
                  className="hidden sm:block text-sm font-medium text-muted-foreground hover:text-foreground transition-colors px-3 py-2"
                >
                  Войти
                </button>
                <button
                  onClick={() => openAuth("register")}
                  className="px-4 py-2 rounded-full bg-white text-black text-sm font-semibold hover:bg-rating transition-colors"
                >
                  Регистрация
                </button>
              </div>
            )}
          </div>

          {/* Мобильная навигация */}
          <nav className="md:hidden flex items-center gap-1 pb-2 -mt-1">
            <NavButton
              icon={<Flame className="h-4 w-4" />}
              label="Подборки"
              href="/"
              active={activeName === "feed"}
              onClick={() => setView({ name: "feed" })}
            />
            <NavButton
              icon={<LayoutGrid className="h-4 w-4" />}
              label="Каталог"
              href="/catalog"
              active={activeName === "catalog"}
              onClick={() => setView({ name: "catalog" })}
            />
          </nav>
        </div>
      </header>

      <AuthModal
        open={authOpen}
        onClose={() => setAuthOpen(false)}
        initialMode={authMode}
      />
    </>
  );
}

function NavButton({
  icon,
  label,
  href,
  active,
  onClick,
}: {
  icon: React.ReactNode;
  label: string;
  href: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <a
      href={href}
      onClick={onClick}
      className={cn(
        "flex items-center gap-2 px-3.5 py-2 rounded-lg text-sm font-medium transition-all relative",
        active
          ? "bg-white/10 text-foreground"
          : "text-muted-foreground hover:text-foreground hover:bg-white/5"
      )}
    >
      {icon}
      {label}
      {active && (
        <motion.span
          layoutId="nav-underline"
          className="absolute -bottom-1 left-3 right-3 h-0.5 bg-rating rounded-full"
        />
      )}
    </a>
  );
}

function DropdownItem({
  icon,
  label,
  badge,
  danger,
  onClick,
}: {
  icon: React.ReactNode;
  label: string;
  badge?: number;
  danger?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "w-full flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm font-medium transition-colors",
        danger
          ? "text-skip hover:bg-skip/10"
          : "text-muted-foreground hover:text-foreground hover:bg-white/5"
      )}
    >
      {icon}
      <span className="flex-1 text-left">{label}</span>
      {badge !== undefined && (
        <span className="text-xs bg-rating/15 text-rating px-1.5 py-0.5 rounded-md font-semibold tabular-nums">
          {badge}
        </span>
      )}
    </button>
  );
}
