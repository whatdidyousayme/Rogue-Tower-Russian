# -*- coding: utf-8 -*-
"""
Rogue Tower Russian — установщик мода v1.2.
Одиночная игра без обновлений: если игра обновится, накатите мод заново.
"""
import os
import sys
import shutil
import threading
import queue
from install_core import install, uninstall, restore_catalog, bepinex_ready, payload_dir
import zipfile
import re
import webbrowser
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

MOD_VERSION = "1.2"
AUTHOR_GITHUB = "https://github.com/whatdidyousayme"
GAME_FOLDER_GUESSES = ["Rogue Tower"]
BEPINEX_URL = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x86_5.4.23.3.zip"
# Целевая версия игры, на которую рассчитан мод (для проверки совместимости).
GAME_VERSION_TARGET = "1.3.2.0"

# Файлы мода, которые доставляем в BepInEx\\plugins.
MOD_FILES = ["RogueTowerRussian.dll", "translations.json"]
# Исходники (открытость мода) — копируются в отдельную папку.
SOURCE_FILES = ["TranslatorPlugin.cs", "build_mod32.py", "changelog.txt"]


def detect_game_version(game_dir):
    """Пытаемся определить версию игры по файлам (степень совместимости)."""
    # Ищем строку вида "Rogue Tower 1.x.y.z" в манифесте/ассетах (лёгкий проход).
    exe = os.path.join(game_dir, "Rogue Tower.exe")
    if os.path.exists(exe):
        try:
            with open(exe, "rb") as f:
                data = f.read(400000)
            m = re.search(rb"Rogue Tower\s+v?(\d+\.\d+\.\d+\.\d+)", data)
            if m:
                return m.group(1).decode("ascii", "replace")
        except Exception:
            pass
    # fallback: app.info
    info = os.path.join(game_dir, "Rogue Tower_Data", "app.info")
    if os.path.exists(info):
        try:
            txt = open(info, encoding="utf-8", errors="replace").read()
            m = re.search(r"(\d+\.\d+\.\d+\.\d+)", txt)
            if m:
                return m.group(1)
        except Exception:
            pass
    # The version is serialized as the title on the main-menu canvas.
    data_dir = os.path.join(game_dir, 'Rogue Tower_Data')
    if os.path.isdir(data_dir):
        for name in ('level0', 'level1', 'sharedassets0.assets', 'sharedassets1.assets'):
            path = os.path.join(data_dir, name)
            if os.path.isfile(path):
                with open(path, 'rb') as stream:
                    match = re.search(rb'Rogue Tower\s+v?(\d+\.\d+\.\d+\.\d+)', stream.read(6 * 1024 * 1024))
                if match:
                    return match.group(1).decode('ascii')
    return None



def find_steam_library_roots():
    """Корни Steam-библиотек (registry + libraryfolders.vdf)."""
    roots = []
    try:
        import winreg
        for root in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
            try:
                key = winreg.OpenKey(root, r"SOFTWARE\Valve\Steam")
                steam_path, _ = winreg.QueryValueEx(key, "SteamPath")
                if steam_path:
                    roots.append(os.path.join(steam_path, "steamapps"))
            except OSError:
                pass
        for steamapps in list(roots):
            vdf = os.path.join(steamapps, "libraryfolders.vdf")
            if os.path.exists(vdf):
                try:
                    with open(vdf, "r", encoding="utf-8", errors="replace") as f:
                        text = f.read()
                    for m in re.finditer(r'"path"\s+"([^"]+)"', text):
                        p = m.group(1).replace("\\\\", "\\")
                        roots.append(os.path.join(p.strip(), "steamapps"))
                except Exception:
                    pass
    except Exception:
        pass
    seen = set()
    out = []
    for r in roots:
        rl = os.path.normpath(r).lower()
        if rl not in seen:
            seen.add(rl)
            out.append(r)
    return out


def find_game_folder():
    """Авто-поиск папки установки Rogue Tower."""
    here = os.path.dirname(os.path.abspath(__file__))
    if os.path.exists(os.path.join(here, "Rogue Tower.exe")):
        return here
    for steamapps in find_steam_library_roots():
        common = os.path.join(steamapps, "common")
        for guess in GAME_FOLDER_GUESSES:
            cand = os.path.join(common, guess)
            if os.path.exists(os.path.join(cand, "Rogue Tower.exe")):
                return cand
    for drive in ("C:", "D:", "E:", "F:"):
        for sub in (r"Program Files (x86)\Steam\steamapps\common\Rogue Tower",
                    r"SteamLibrary\steamapps\common\Rogue Tower"):
            cand = os.path.join(drive + os.sep, sub)
            if os.path.exists(os.path.join(cand, "Rogue Tower.exe")):
                return cand
    return None


def find_window_icon():
    """Поиск .ico для иконки окна: в сборке (sys._MEIPASS) или рядом с exe."""
    candidates = []
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        candidates.append(os.path.join(meipass, "payload", "icon.ico"))
    candidates.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.ico"))
    for p in candidates:
        if os.path.exists(p):
            return p
    return None


class InstallerApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Rogue Tower Russian — установщик")
        self.geometry("930x650")
        self.resizable(False, False)
        # Иконка окна (в заголовке) — жёлтая башня из игры.
        _ico = find_window_icon()
        if _ico:
            try:
                self.iconbitmap(_ico)
            except Exception:
                pass
        self.auto_path = find_game_folder()
        self.game_path = tk.StringVar(value=self.auto_path or "")
        self.status = tk.StringVar(value="Готов к работе.")
        self.events = queue.Queue()
        self.busy = False
        self._build_ui()
        self.after(100, self._poll_events)

    def _build_ui(self):
        header = tk.Frame(self, bg="#2d3a4f")
        header.pack(fill="x")
        tk.Label(header, text="Rogue Tower Russian Translator",
                 font=("Segoe UI", 15, "bold"), fg="white", bg="#2d3a4f", pady=12).pack()

        body = tk.Frame(self, padx=16, pady=6)
        body.pack(fill="both", expand=True)

        info = ttk.LabelFrame(body, text="О моде", padding=8)
        info.pack(fill="x", pady=4)
        desc = (
            "Полный перевод игры Rogue Tower на русский язык:\n"
            "— все тексты, карточки, описания, пасхалки, имена боссов;\n"
            "— перевод включается автоматически при запуске игры;\n"
            "— справа вверху: ВКЛ/ВЫКЛ перевода и ползунок масштаба текста.\n\n"
            "Мод создан с помощью ИИ и открыт для редактирования.\n"
            "Версия мода: %s\n"
            "Проект на GitHub: %s" % (MOD_VERSION, AUTHOR_GITHUB)
        )
        tk.Label(info, text=desc, justify="left", anchor="w", wraplength=580,
                 font=("Segoe UI", 9)).pack(anchor="w")
        # Ссылка на GitHub (открывается в браузере).
        link = tk.Label(info, text=AUTHOR_GITHUB, fg="#1a5fb4", cursor="hand2", font=("Segoe UI", 9, "underline"))
        link.pack(anchor="w")
        link.bind("<Button-1>", lambda e: self._open_url(AUTHOR_GITHUB))

        warn = tk.Label(body, text=("Перед установкой закройте игру.\n"
                                    "Если игра когда-то обновится — установите мод заново."),
                        fg="#7a3b00", bg="#fff3e0", justify="left", anchor="w", pady=6, padx=8)
        warn.pack(fill="x", pady=4)

        pathf = ttk.LabelFrame(body, text="Папка с игрой", padding=6)
        pathf.pack(fill="x", pady=6)
        row = tk.Frame(pathf)
        row.pack(fill="x")
        tk.Entry(row, textvariable=self.game_path, width=46).pack(side="left", fill="x", expand=True)
        ttk.Button(row, text="Обзор...", command=self._browse).pack(side="left", padx=6)
        self.bex_label = tk.Label(pathf, text="", anchor="w", justify="left", fg="#444")
        self.bex_label.pack(fill="x", pady=(4, 0))
        self.ver_label = tk.Label(pathf, text="", anchor="w", justify="left", fg="#666")
        self.ver_label.pack(fill="x", pady=(2, 0))
        self._check_ver()

        btns = tk.Frame(body, pady=8)
        btns.pack(fill="x")
        ttk.Button(btns, text="Установить мод", command=self._install).pack(side="left")
        ttk.Button(btns, text="Удалить мод", command=self._uninstall).pack(side="left", padx=6)
        ttk.Button(btns, text="Восстановить словарь", command=self._restore_backup).pack(side="left", padx=6)
        ttk.Button(btns, text="Проверить обновление", command=self._check_update).pack(side="left", padx=6)
        ttk.Button(btns, text="Открыть папку игры", command=self._open_folder).pack(side="left", padx=6)
        ttk.Button(btns, text="Выход", command=self.quit).pack(side="right")


        tk.Label(self, textvariable=self.status, anchor="w", fg="#2d6a2d",
                 font=("Segoe UI", 9)).pack(fill="x", side="bottom", padx=12, pady=6)
        self._check_bep()

    def _check_bep(self):
        p = self.game_path.get()
        if p and bepinex_ready(p):
            self.bex_label.config(text="✓ BepInEx уже установлен.", fg="#2d6a2d")
        else:
            self.bex_label.config(text="— BepInEx не установлен или неполон. Он будет установлен из встроенного архива.", fg="#7a3b00")

    def _check_ver(self):
        p = self.game_path.get()
        if not p:
            self.ver_label.config(text="")
            return
        gv = detect_game_version(p)
        if gv:
            if gv == GAME_VERSION_TARGET:
                self.ver_label.config(text="Версия игры: %s (совместима с модом)" % gv, fg="#2d6a2d")
            else:
                self.ver_label.config(
                    text="Версия игры: %s (мод рассчитан на %s — возможны несоответствия)" % (gv, GAME_VERSION_TARGET),
                    fg="#7a3b00")
        else:
            self.ver_label.config(text="Версия не определена; установщик проверит папку и разрядность игры.", fg="#7a3b00")

    def _set_status(self, s):
        self.status.set(s)

    def _browse(self):
        d = filedialog.askdirectory(title="Выберите папку с Rogue Tower")
        if d:
            self.game_path.set(d)
            self._check_bep()
            self._check_ver()

    def _open_folder(self):
        p = self.game_path.get()
        if not p:
            messagebox.showwarning("Путь не задан", "Сначала укажите папку с игрой.")
            return
        try:
            os.startfile(p)
        except Exception as e:
            messagebox.showerror("Ошибка", str(e))

    def _open_url(self, url):
        try:
            webbrowser.open(url)
        except Exception as e:
            messagebox.showerror("Ошибка", "Не удалось открыть ссылку: %s" % e)

    def _poll_events(self):
        try:
            while True:
                kind, text = self.events.get_nowait()
                self._set_status(text)
                if kind in ("done", "error"):
                    self.busy = False
                    self._check_bep()
                    if kind == "error":
                        messagebox.showerror("Ошибка установки", text)
                    else:
                        messagebox.showinfo("Готово", text)
        except queue.Empty:
            pass
        self.after(100, self._poll_events)

    def _install(self):
        if self.busy:
            return
        p = self.game_path.get().strip()
        self.busy = True
        self._set_status("Проверка установщика...")
        def worker():
            try:
                install(p, progress=lambda text: self.events.put(("progress", text)))
                self.events.put(("done", "Мод и BepInEx установлены и проверены. Запустите игру через Steam.\n"
                    "Словарь: BepInEx\\plugins\\translations.json\n"
                    "Резервные копии: RogueTowerRussian_backups"))
            except Exception as error:
                self.events.put(("error", str(error)))
        threading.Thread(target=worker, daemon=True).start()

    def _restore_backup(self):
        if self.busy:
            return
        try:
            restore_catalog(self.game_path.get().strip())
            self._set_status("Исходный словарь восстановлен. Ваша копия сохранена в RogueTowerRussian_backups.")
        except Exception as error:
            messagebox.showerror("Ошибка", str(error))

    def _uninstall(self):
        if self.busy:
            return
        if not messagebox.askyesno("Удаление", "Удалить русский перевод? BepInEx и резервные копии сохранятся."):
            return
        try:
            uninstall(self.game_path.get().strip())
            self._set_status("Русский перевод удалён.")
            self._check_bep()
        except Exception as error:
            messagebox.showerror("Ошибка", str(error))

    def _check_update(self):
        self._open_url(AUTHOR_GITHUB)

def main():
    InstallerApp().mainloop()

if __name__ == "__main__":
    main()
