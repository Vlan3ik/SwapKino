"use client";

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { X, Mail, Lock, User, Flame, Eye, EyeOff } from "lucide-react";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";

interface AuthModalProps {
  open: boolean;
  onClose: () => void;
  /** начальная вкладка */
  initialMode?: "login" | "register";
}

export function AuthModal({ open, onClose, initialMode = "login" }: AuthModalProps) {
  const { login, register } = useAppStore();
  const [mode, setMode] = useState<"login" | "register">(initialMode);
  const [identifier, setIdentifier] = useState("");
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const passwordRules = [
    { label: "не менее 8 символов", valid: password.length >= 8 },
    { label: "строчная буква", valid: /[a-zа-я]/.test(password) },
    { label: "заглавная буква", valid: /[A-ZА-Я]/.test(password) },
    { label: "цифра", valid: /\d/.test(password) },
    { label: "спецсимвол", valid: /[^\p{L}\p{N}]/u.test(password) },
  ];
  const passwordIsStrong = passwordRules.every((rule) => rule.valid);

  const reset = () => {
    setIdentifier("");
    setUsername("");
    setEmail("");
    setPassword("");
    setError(null);
    setShowPassword(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (mode === "register" && !passwordIsStrong) {
      const missing = passwordRules.filter((rule) => !rule.valid).map((rule) => rule.label);
      setError(`Пароль не надёжен. Добавь: ${missing.join(", ")}.`);
      return;
    }
    setLoading(true);
    const result =
      mode === "login"
        ? await login(identifier, password)
        : await register({ username, email, password });
    setLoading(false);
    if (result.ok) {
      reset();
      onClose();
    } else {
      setError(result.error);
    }
  };

  const switchMode = (m: "login" | "register") => {
    setMode(m);
    setError(null);
  };

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[100] flex items-center justify-center p-4"
        >
          {/* Бэкдроп */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="absolute inset-0 bg-black/80 backdrop-blur-md"
          />

          {/* Модальное окно */}
          <motion.div
            initial={{ opacity: 0, y: 20, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 20, scale: 0.96 }}
            transition={{ type: "spring", stiffness: 280, damping: 26 }}
            className="relative w-full max-w-md glass-panel-strong rounded-3xl p-6 sm:p-8 shadow-cinematic"
          >
            {/* Фоновое свечение */}
            <div className="absolute -top-20 -right-20 w-48 h-48 bg-rating/15 rounded-full blur-3xl pointer-events-none" />
            <div className="absolute -bottom-20 -left-20 w-48 h-48 bg-like/10 rounded-full blur-3xl pointer-events-none" />

            {/* Закрыть */}
            <button
              onClick={onClose}
              className="absolute top-4 right-4 h-8 w-8 rounded-full flex items-center justify-center text-muted-foreground hover:text-foreground hover:bg-white/5 transition-colors"
              aria-label="Закрыть"
            >
              <X className="h-4 w-4" />
            </button>

            {/* Лого */}
            <div className="flex items-center gap-2.5 mb-1">
              <Flame className="h-7 w-7 text-rating" fill="currentColor" />
              <span className="text-xl font-bold tracking-tight">СвайпКино</span>
            </div>

            {/* Переключатель режимов */}
            <div className="flex gap-1 p-1 bg-black/30 rounded-xl mt-5 mb-6">
              <ModeTab
                active={mode === "login"}
                onClick={() => switchMode("login")}
                label="Вход"
              />
              <ModeTab
                active={mode === "register"}
                onClick={() => switchMode("register")}
                label="Регистрация"
              />
            </div>

            <form onSubmit={handleSubmit} className="space-y-3">
              {mode === "register" && (
                <>
                  <Field
                    icon={<User className="h-4 w-4" />}
                    type="text"
                    placeholder="Имя пользователя"
                    value={username}
                    onChange={setUsername}
                    autoFocus
                  />
                  <Field
                    icon={<Mail className="h-4 w-4" />}
                    type="email"
                    placeholder="Email"
                    value={email}
                    onChange={setEmail}
                  />
                </>
              )}
              {mode === "login" && (
                <Field
                  icon={<User className="h-4 w-4" />}
                  type="text"
                  placeholder="Email или имя пользователя"
                  value={identifier}
                  onChange={setIdentifier}
                  autoFocus
                />
              )}
              <div className="relative">
                <Field
                  icon={<Lock className="h-4 w-4" />}
                  type={showPassword ? "text" : "password"}
                  placeholder="Пароль"
                  value={password}
                  onChange={setPassword}
                  minLength={mode === "register" ? 8 : undefined}
                  autoComplete={mode === "register" ? "new-password" : "current-password"}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                >
                  {showPassword ? (
                    <EyeOff className="h-4 w-4" />
                  ) : (
                    <Eye className="h-4 w-4" />
                  )}
                </button>
              </div>
              {mode === "register" && (
                <div className="rounded-xl border border-white/10 bg-black/20 px-3 py-2.5 space-y-1.5">
                  <p className={cn("text-xs font-semibold", passwordIsStrong ? "text-like" : "text-muted-foreground")}>
                    {passwordIsStrong ? "Пароль надёжный" : "Пароль не надёжен"}
                  </p>
                  <div className="grid grid-cols-2 gap-x-3 gap-y-1">
                    {passwordRules.map((rule) => (
                      <span key={rule.label} className={cn("text-[11px]", rule.valid ? "text-like" : "text-muted-foreground")}>
                        {rule.valid ? "✓" : "○"} {rule.label}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {error && (
                <motion.div
                  initial={{ opacity: 0, y: -5 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="text-xs text-skip bg-skip/10 border border-skip/20 rounded-lg px-3 py-2"
                >
                  {error}
                </motion.div>
              )}

              <button
                type="submit"
                disabled={loading}
                className={cn(
                  "w-full py-3 rounded-xl font-semibold text-sm transition-all mt-2",
                  "bg-white text-black hover:bg-rating",
                  "disabled:opacity-60 disabled:cursor-not-allowed"
                )}
              >
                {loading
                  ? "Подождите…"
                  : mode === "login"
                  ? "Войти"
                  : "Создать аккаунт"}
              </button>
            </form>

            <p className="text-[10px] text-muted-foreground/70 text-center mt-4 leading-relaxed">
              Данные аккаунта и действия сохраняются на сервере. Войди, чтобы
              синхронизировать оценки с Кинопоиском.
            </p>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function ModeTab({
  active,
  onClick,
  label,
}: {
  active: boolean;
  onClick: () => void;
  label: string;
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "flex-1 py-2 rounded-lg text-sm font-medium transition-all",
        active
          ? "bg-white text-black shadow"
          : "text-muted-foreground hover:text-foreground"
      )}
    >
      {label}
    </button>
  );
}

function Field({
  icon,
  type,
  placeholder,
  value,
  onChange,
  autoFocus,
  minLength,
  autoComplete,
}: {
  icon: React.ReactNode;
  type: string;
  placeholder: string;
  value: string;
  onChange: (v: string) => void;
  autoFocus?: boolean;
  minLength?: number;
  autoComplete?: string;
}) {
  return (
    <div className="relative">
      <span className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-foreground">
        {icon}
      </span>
      <input
        type={type}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoFocus={autoFocus}
        minLength={minLength}
        autoComplete={autoComplete}
        className="w-full bg-black/30 border border-white/10 rounded-xl pl-10 pr-4 py-3 text-sm placeholder:text-muted-foreground outline-none focus:ring-2 focus:ring-rating/30 transition-all"
      />
    </div>
  );
}
