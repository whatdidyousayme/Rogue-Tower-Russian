# -*- coding: utf-8 -*-
"""
Rogue Tower Russian — установщик мода v1.0.
Одиночная игра без обновлений: если игра обновится, накатите мод заново.
"""
import os
import sys
import shutil
import urllib.request
import zipfile
import re
import webbrowser
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

MOD_VERSION = "1.0"
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
        candidates.append(os.path.join(meipass, "icon.ico"))
    candidates.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.ico"))
    for p in candidates:
        if os.path.exists(p):
            return p
    return None


def resource_dirs():
    """Каталоги, где ищем файлы мода: встроенные в .exe (onefile), рядом с exe, рядом со скриптом."""
    dirs = []
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        dirs.append(meipass)
    if getattr(sys, "frozen", False):
        dirs.append(os.path.dirname(os.path.abspath(sys.executable)))
    dirs.append(os.path.dirname(os.path.abspath(__file__)))
    seen = set()
    out = []
    for d in dirs:
        nd = os.path.normpath(d)
        if nd not in seen:
            seen.add(nd)
            out.append(d)
    return out



class InstallerApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Rogue Tower Russian — установщик")
        self.geometry("640x580")
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
        self._build_ui()

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
            "— в игре в правом верхнем углу есть кнопка ВКЛ/ВЫКЛ перевода.\n\n"
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

        warn = tk.Label(body, text=("⚠  Игра одиночная и уже не получает обновлений.\n"
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
        ttk.Button(btns, text="Вернуть бэкап", command=self._restore_backup).pack(side="left", padx=6)
        ttk.Button(btns, text="Проверить обновление", command=self._check_update).pack(side="left", padx=6)
        ttk.Button(btns, text="Открыть папку игры", command=self._open_folder).pack(side="left", padx=6)
        ttk.Button(btns, text="Выход", command=self.quit).pack(side="right")


        tk.Label(self, textvariable=self.status, anchor="w", fg="#2d6a2d",
                 font=("Segoe UI", 9)).pack(fill="x", side="bottom", padx=12, pady=6)
        self._check_bep()

    def _check_bep(self):
        p = self.game_path.get()
        if p and os.path.exists(os.path.join(p, "BepInEx", "core", "BepInEx.dll")):
            self.bex_label.config(text="✓ BepInEx уже установлен.", fg="#2d6a2d")
        else:
            self.bex_label.config(text="— BepInEx не найден. Установщик скачает и поставит его.", fg="#7a3b00")

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
            self.ver_label.config(text="Версию игры не удалось определить автоматически.", fg="#7a3b00")

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

    def _install(self):
        p = self.game_path.get().strip()
        if not p:
            messagebox.showwarning("Путь", "Укажите папку с Rogue Tower.")
            return
        if not os.path.exists(os.path.join(p, "Rogue Tower.exe")):
            if not messagebox.askyesno("Проверка",
                                       "В этой папке не найден Rogue Tower.exe.\nПродолжить установку всё равно?"):
                return
        try:
            # Бэкап имеющихся файлов мода перед установкой (чтобы можно было откатить).
            self._backup_mod(p)
            self._set_status("Установка BepInEx (если нужно)...")
            self.update_idletasks()
            self._ensure_bepinex(p)
            self._set_status("Копирование файлов мода...")
            self.update_idletasks()
            self._copy_mod(p)
            self._copy_sources(p)
            self._set_status("Готово!")
            messagebox.showinfo(
                "Успешно",
                "Мод установлен!\n\n"
                "Запустите игру через Steam — перевод включится автоматически.\n"
                "Кнопка перевода — в правом верхнем углу в игре.\n\n"
                "Мод открыт для редактирования: словарь переводов находится в\n"
                "BepInEx\\plugins\\translations.json — его можно править.\n"
                "Исходники — в папке RogueTowerRussian_sources.\n\n"
                "Проверить обновления: кнопка «Проверить обновление» (откроет GitHub).\n"
                "Если игра обновится, накатите мод повторно.")
        except Exception as e:
            self._set_status("ОШИБКА: " + str(e))
            messagebox.showerror("Ошибка установки", str(e))

    def _backup_mod(self, game_dir):
        """Создаёт резервную копию существующих файлов мода (при повторной установке)."""
        plugins_dir = os.path.join(game_dir, "BepInEx", "plugins")
        backup_dir = os.path.join(game_dir, "BepInEx", "plugins", "_backup")
        if not os.path.exists(plugins_dir):
            return
        made = False
        for f in MOD_FILES:
            src = os.path.join(plugins_dir, f)
            if os.path.exists(src):
                os.makedirs(backup_dir, exist_ok=True)
                # Не затираем более ранний бэкап: добавляем номер.
                stem, ext = os.path.splitext(f)
                dst = os.path.join(backup_dir, f)
                i = 1
                while os.path.exists(dst):
                    dst = os.path.join(backup_dir, "%s.bak%d%s" % (stem, i, ext))
                    i += 1
                shutil.copy2(src, dst)
                made = True
        if made:
            self._set_status("Создан бэкап предыдущего мода в BepInEx\\plugins\\_backup.")

    def _restore_backup(self):
        """Восстанавливает исходные (заводские) файлы мода из бэкапа."""
        p = self.game_path.get().strip()
        if not p:
            messagebox.showwarning("Путь", "Укажите папку с Rogue Tower.")
            return
        backup_dir = os.path.join(p, "BepInEx", "plugins", "_backup")
        if not os.path.exists(backup_dir):
            messagebox.showinfo("Бэкап", "Резервных копий не найдено.\n"
                                         "Бэкап создаётся автоматически при повторной установке мода.")
            return
        restored = []
        for f in MOD_FILES:
            # Ищем самый свежий .bakN файл-бэкап, иначе обычный.
            cands = [os.path.join(backup_dir, f)]
            cands += [os.path.join(backup_dir, n) for n in os.listdir(backup_dir)
                      if n.startswith(os.path.splitext(f)[0] + ".bak") and n.endswith(os.path.splitext(f)[1])]
            # Берём существующий (первый по списку — обычный, предпочитаем его).
            src = next((c for c in cands if os.path.exists(c)), None)
            if src:
                dst = os.path.join(p, "BepInEx", "plugins", f)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(src, dst)
                restored.append(f)
        if restored:
            self._set_status("Восстановлено из бэкапа: " + ", ".join(restored))
            messagebox.showinfo("Бэкап восстановлен",
                                "Восстановлены заводские файлы:\n%s\n\n"
                                "Мод вернулся к исходному (нередактированному) состоянию." % ", ".join(restored))
        else:
            self._set_status("В бэкапе нет подходящих файлов.")
            messagebox.showinfo("Бэкап", "Не удалось найти файлы для восстановления.")

    def _uninstall(self):
        p = self.game_path.get().strip()
        if not p:
            messagebox.showwarning("Путь", "Укажите папку с Rogue Tower.")
            return
        if not messagebox.askyesno("Удаление",
                                   "Удалить мод ПОЛНОСТЬЮ (включая BepInEx, файлы мода,\n"
                                   "исходники и бэкап)?\n\n"
                                   "Игра вернётся к исходному (английскому) виду."):
            return
        removed = []

        # Файлы мода.
        plugins_dir = os.path.join(p, "BepInEx", "plugins")
        for f in MOD_FILES:
            fp = os.path.join(plugins_dir, f)
            if os.path.exists(fp):
                try:
                    os.remove(fp)
                    removed.append(f)
                except Exception as e:
                    messagebox.showerror("Ошибка", "Не удалось удалить %s: %s" % (f, e))
                    return

        # Исходники, оставленные при установке.
        src_dir = os.path.join(p, "RogueTowerRussian_sources")
        if os.path.exists(src_dir):
            shutil.rmtree(src_dir, ignore_errors=True)
            removed.append("RogueTowerRussian_sources")

        # Файлы BepInEx (doorstop + библиотеки).
        doorstop_files = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx"]
        for name in doorstop_files:
            fp = os.path.join(p, name)
            if os.path.isdir(fp):
                shutil.rmtree(fp, ignore_errors=True)
                removed.append(name)
            elif os.path.exists(fp):
                try:
                    os.remove(fp)
                    removed.append(name)
                except Exception as e:
                    messagebox.showerror("Ошибка", "Не удалось удалить %s: %s" % (name, e))
                    return

        if removed:
            self._set_status("Мод полностью удалён: " + ", ".join(removed))
            messagebox.showinfo("Удаление завершено",
                                "Мод полностью удалён:\n%s\n\n"
                                "Игра снова на английском." % ", ".join(removed))
        else:
            self._set_status("Мод не был установлен.")
            messagebox.showinfo("Удаление", "Файлы мода не найдены — удалять нечего.")

    def _check_update(self):
        # Открываем страницу проекта на GitHub (там релизы/обновления).
        self._open_url(AUTHOR_GITHUB)

    def _ensure_bepinex(self, game_dir):
        plugin_dll = os.path.join(game_dir, "BepInEx", "core", "BepInEx.dll")
        if os.path.exists(plugin_dll):
            return
        bundled = None
        try:
            meipass = getattr(sys, "_MEIPASS", None)
            if meipass:
                cand = os.path.join(meipass, "bepinex_core.zip")
                if os.path.exists(cand):
                    bundled = cand
        except Exception:
            pass
        if bundled is None:
            cand = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bepinex_core.zip")
            if os.path.exists(cand):
                bundled = cand
        if bundled is not None:
            self._set_status("Распаковка BepInEx (встроенный архив)...")
            self.update_idletasks()
            with zipfile.ZipFile(bundled, "r") as z:
                z.extractall(game_dir)
        else:
            self._set_status("Скачивание BepInEx...")
            self.update_idletasks()
            zip_path = os.path.join(game_dir, "bepinex_x86.zip")
            try:
                urllib.request.urlretrieve(BEPINEX_URL, zip_path)
            except Exception as e:
                raise RuntimeError("Не удалось скачать BepInEx: %s. Проверьте интернет." % e)
            try:
                with zipfile.ZipFile(zip_path, "r") as z:
                    z.extractall(game_dir)
            finally:
                if os.path.exists(zip_path):
                    os.remove(zip_path)
        if not os.path.exists(plugin_dll):
            raise RuntimeError("BepInEx распакован, но BepInEx.dll не найден.")

    def _copy_mod(self, game_dir):
        plugins_dir = os.path.join(game_dir, "BepInEx", "plugins")
        os.makedirs(plugins_dir, exist_ok=True)
        # Эталонная копия заводских файлов: позволяет вернуть исходную версию
        # после того, как пользователь изменит словарь.
        backup_dir = os.path.join(plugins_dir, "_backup")
        for f in MOD_FILES:
            src = next((os.path.join(d, f) for d in resource_dirs()
                        if os.path.exists(os.path.join(d, f))), None)
            if src:
                shutil.copy2(src, os.path.join(plugins_dir, f))
                os.makedirs(backup_dir, exist_ok=True)
                shutil.copy2(src, os.path.join(backup_dir, f))
            else:
                print("Предупреждение: не найден файл мода", f)

    def _copy_sources(self, game_dir):
        dest = os.path.join(game_dir, "RogueTowerRussian_sources")
        os.makedirs(dest, exist_ok=True)
        for f in SOURCE_FILES:
            src = next((os.path.join(d, f) for d in resource_dirs()
                        if os.path.exists(os.path.join(d, f))), None)
            if src:
                shutil.copy2(src, os.path.join(dest, f))


def main():
    app = InstallerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
