"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { ArrowLeft, Check, Eye, EyeOff, KeyRound, Save, Settings, ShieldAlert, Trash2, Upload, UserRound, X } from "lucide-react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useAppStore } from "@/lib/store";
import { AvatarCropModal } from "./AvatarCropModal";

export function ProfileSettingsView() {
  const router = useRouter();
  const { user, updateUserProfile, deleteAccount } = useAppStore();
  const [displayName, setDisplayName] = useState(user?.username ?? "");
  const [avatarUrl, setAvatarUrl] = useState(user?.avatarUrl ?? "");
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [repeatPassword, setRepeatPassword] = useState("");
  const [showPasswords, setShowPasswords] = useState(false);
  const [profileMessage, setProfileMessage] = useState<string | null>(null);
  const [passwordMessage, setPasswordMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState<"profile" | "password" | "delete" | null>(null);
  const [avatarBusy, setAvatarBusy] = useState(false);
  const [avatarMessage, setAvatarMessage] = useState<string | null>(null);
  const [cropFile, setCropFile] = useState<File | null>(null);
  const avatarInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!user) router.replace("/profile");
  }, [router, user]);

  if (!user) return null;

  const saveProfile = async (event: FormEvent) => {
    event.preventDefault();
    setBusy("profile"); setProfileMessage(null);
    try {
      const updated = await api.updateProfile({ displayName: displayName.trim() });
      updateUserProfile(updated);
      setProfileMessage("Профиль сохранён");
    } catch (error) { setProfileMessage(error instanceof Error ? error.message : "Не удалось сохранить профиль"); }
    finally { setBusy(null); }
  };

  const uploadAvatar = async (file?: File) => {
    if (!file) return;
    setAvatarMessage(null);
    if (!file.type.startsWith("image/") || !["image/jpeg", "image/png", "image/webp", "image/gif"].includes(file.type)) { setAvatarMessage("Выбери JPG, PNG, WEBP или GIF"); return; }
    if (file.size > 5 * 1024 * 1024) { setAvatarMessage("Файл должен быть не больше 5 МБ"); return; }
    setAvatarBusy(true);
    try { const updated = await api.uploadAvatar(file); updateUserProfile(updated); setAvatarUrl(updated.avatarUrl ?? ""); setAvatarMessage("Аватарка обновлена"); }
    catch (error) { setAvatarMessage(error instanceof Error ? error.message : "Не удалось загрузить аватарку"); }
    finally { setAvatarBusy(false); }
  };

  const selectAvatar = (file?: File) => {
    if (!file) return;
    if (!file.type.startsWith("image/") || !["image/jpeg", "image/png", "image/webp", "image/gif"].includes(file.type)) { setAvatarMessage("Выбери JPG, PNG, WEBP или GIF"); return; }
    if (file.size > 5 * 1024 * 1024) { setAvatarMessage("Файл должен быть не больше 5 МБ"); return; }
    setAvatarMessage(null); setCropFile(file);
  };

  const removeAvatar = async () => {
    setAvatarBusy(true); setAvatarMessage(null);
    try { await api.deleteAvatar(); updateUserProfile({ ...user, displayName: user.username, avatarUrl: null, createdAt: new Date(user.createdAt).toISOString() }); setAvatarUrl(""); setAvatarMessage("Аватарка удалена"); }
    catch (error) { setAvatarMessage(error instanceof Error ? error.message : "Не удалось удалить аватарку"); }
    finally { setAvatarBusy(false); }
  };

  const savePassword = async (event: FormEvent) => {
    event.preventDefault();
    setPasswordMessage(null);
    if (newPassword !== repeatPassword) { setPasswordMessage("Новые пароли не совпадают"); return; }
    if (newPassword.length < 8) { setPasswordMessage("Новый пароль должен быть не короче 8 символов"); return; }
    setBusy("password");
    try {
      await api.changePassword({ currentPassword, newPassword });
      setCurrentPassword(""); setNewPassword(""); setRepeatPassword("");
      setPasswordMessage("Пароль изменён. Войдите заново на других устройствах.");
    } catch (error) { setPasswordMessage(error instanceof Error ? error.message : "Не удалось изменить пароль"); }
    finally { setBusy(null); }
  };

  const removeAccount = async () => {
    if (!window.confirm("Удалить аккаунт и все профильные данные без возможности восстановления?")) return;
    setBusy("delete");
    const result = await deleteAccount();
    if (result.ok) router.replace("/");
    else { setPasswordMessage(result.error); setBusy(null); }
  };

  const avatar = avatarUrl.trim();
  return <><main className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8 py-6 sm:py-10 space-y-5">
    <button type="button" onClick={() => router.push("/profile")} className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4" /> Вернуться в профиль</button>
    <div className="flex items-start gap-4"><div className="rounded-2xl bg-rating/15 p-3 text-rating"><Settings className="h-6 w-6" /></div><div><h1 className="text-2xl sm:text-3xl font-bold">Настройки профиля</h1><p className="mt-1 text-sm text-muted-foreground">Управляй именем, аватаром, паролем и безопасностью аккаунта.</p></div></div>

    <section className="glass-panel rounded-3xl p-5 sm:p-7">
      <div className="flex items-center gap-3 mb-5"><UserRound className="h-5 w-5 text-rating" /><div><h2 className="text-lg font-bold">Профиль</h2><p className="text-xs text-muted-foreground">Эти данные видны только в интерфейсе СвайпКино.</p></div></div>
      <form onSubmit={saveProfile} className="space-y-4">
          <div onDragOver={event => event.preventDefault()} onDrop={event => { event.preventDefault(); selectAvatar(event.dataTransfer.files?.[0]); }} className="flex flex-col sm:flex-row sm:items-center gap-4 rounded-2xl border border-dashed border-white/15 bg-black/20 p-4 transition hover:border-rating/50">
          <div className="h-16 w-16 shrink-0 overflow-hidden rounded-full bg-gradient-to-br from-rating/80 to-skip/60 flex items-center justify-center"><AvatarPreview src={avatar} name={displayName || user.username} /></div>
          <div className="min-w-0 flex-1"><p className="text-sm font-semibold">Аватарка</p><p className="mt-1 text-xs text-muted-foreground">Перетащи изображение сюда или выбери файл. После этого можно точно выбрать квадрат.</p><div className="mt-3 flex flex-wrap gap-2"><button type="button" disabled={avatarBusy} onClick={() => avatarInput.current?.click()} className="inline-flex items-center gap-2 rounded-xl bg-white px-3 py-2 text-xs font-bold text-black hover:bg-rating disabled:opacity-50"><Upload className="h-3.5 w-3.5" /> {avatarBusy ? "Загружаем…" : "Выбрать файл"}</button>{avatar && <button type="button" disabled={avatarBusy} onClick={() => void removeAvatar()} className="inline-flex items-center gap-2 rounded-xl border border-white/10 px-3 py-2 text-xs font-semibold text-muted-foreground hover:text-foreground disabled:opacity-50"><X className="h-3.5 w-3.5" /> Удалить</button>}</div><input ref={avatarInput} type="file" accept="image/jpeg,image/png,image/webp,image/gif" className="hidden" onChange={e => { selectAvatar(e.target.files?.[0]); e.currentTarget.value = ""; }} />{avatarMessage && <p className="mt-2 text-xs text-muted-foreground">{avatarMessage}</p>}</div>
        </div>
        <label className="block text-sm font-medium">Имя<input value={displayName} onChange={e => setDisplayName(e.target.value)} maxLength={80} className="mt-2 w-full rounded-xl border border-white/10 bg-black/30 px-4 py-3 outline-none focus:ring-2 focus:ring-rating/40" placeholder="Как тебя называть" /></label>
        <div className="flex items-center gap-3"><button disabled={busy !== null} className="inline-flex items-center gap-2 rounded-xl bg-white px-4 py-2.5 text-sm font-bold text-black hover:bg-rating disabled:opacity-50"><Save className="h-4 w-4" /> Сохранить</button>{profileMessage && <span className="inline-flex items-center gap-1.5 text-sm text-like"><Check className="h-4 w-4" /> {profileMessage}</span>}</div>
      </form>
    </section>

    <section className="glass-panel rounded-3xl p-5 sm:p-7">
      <div className="flex items-center gap-3 mb-5"><KeyRound className="h-5 w-5 text-rating" /><div><h2 className="text-lg font-bold">Смена пароля</h2><p className="text-xs text-muted-foreground">После смены активные сессии на других устройствах будут закрыты.</p></div></div>
      <form onSubmit={savePassword} className="space-y-4">
        <PasswordInput label="Текущий пароль" value={currentPassword} onChange={setCurrentPassword} visible={showPasswords} />
        <PasswordInput label="Новый пароль" value={newPassword} onChange={setNewPassword} visible={showPasswords} />
        <PasswordInput label="Повтори новый пароль" value={repeatPassword} onChange={setRepeatPassword} visible={showPasswords} />
        <div className="flex flex-wrap items-center gap-3"><button type="button" onClick={() => setShowPasswords(v => !v)} className="inline-flex items-center gap-2 text-xs text-muted-foreground hover:text-foreground">{showPasswords ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />} {showPasswords ? "Скрыть пароли" : "Показать пароли"}</button><button disabled={busy !== null || !currentPassword || !newPassword} className="inline-flex items-center gap-2 rounded-xl bg-white px-4 py-2.5 text-sm font-bold text-black hover:bg-rating disabled:opacity-50"><KeyRound className="h-4 w-4" /> Изменить пароль</button></div>
        {passwordMessage && <p className="text-sm text-muted-foreground">{passwordMessage}</p>}
      </form>
    </section>

    <section className="rounded-3xl border border-red-400/20 bg-red-400/5 p-5 sm:p-7"><div className="flex items-start gap-3"><ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-300" /><div><h2 className="text-lg font-bold text-red-200">Удаление аккаунта</h2><p className="mt-1 text-sm text-muted-foreground">Будут удалены профиль, оценки, избранное, история действий, импорт Кинопоиска и активные сессии.</p><button type="button" onClick={() => void removeAccount()} disabled={busy !== null} className="mt-4 inline-flex items-center gap-2 rounded-xl border border-red-300/30 px-4 py-2.5 text-sm font-semibold text-red-200 hover:bg-red-400/10 disabled:opacity-50"><Trash2 className="h-4 w-4" /> {busy === "delete" ? "Удаляем…" : "Удалить аккаунт"}</button></div></div></section>
  </main><AvatarCropModal file={cropFile} onCancel={() => setCropFile(null)} onComplete={file => { setCropFile(null); void uploadAvatar(file); }} /></>;
}

function AvatarPreview({ src, name }: { src: string; name: string }) { return src ? <img src={src} alt="Предпросмотр аватарки" className="h-full w-full object-cover" onError={(event) => { event.currentTarget.style.display = "none"; }} /> : <span className="text-2xl font-bold text-black">{name[0]?.toUpperCase()}</span>; }
function PasswordInput({ label, value, onChange, visible }: { label: string; value: string; onChange: (value: string) => void; visible: boolean }) { return <label className="block text-sm font-medium">{label}<input type={visible ? "text" : "password"} value={value} onChange={e => onChange(e.target.value)} autoComplete="new-password" className="mt-2 w-full rounded-xl border border-white/10 bg-black/30 px-4 py-3 outline-none focus:ring-2 focus:ring-rating/40" /></label>; }
