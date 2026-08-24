using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Newtonsoft.Json;
using BepInEx;
using HarmonyLib;

namespace RogueTowerRussian
{
    [BepInPlugin("com.rogueTower.russian", "Rogue Tower Russian Translator", "1.0.0")]
    public class TranslatorPlugin : BaseUnityPlugin
    {
        private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PrefixTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string PluginDir = Path.Combine(Paths.PluginPath, "");
        private static TMP_FontAsset _cyrillicFont = null;

        public static TMP_FontAsset CyrillicFontAsset
        {
            get
            {
                if (_cyrillicFont == null)
                {
                    // Пробуем несколько системных шрифтов, гарантированно содержащих кириллицу.
                    string[] names = new string[] { "Arial", "Segoe UI", "Times New Roman", "Verdana", "Tahoma" };
                    Font osFont = null;
                    try
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            try { osFont = Font.CreateDynamicFontFromOSFont(names[i], 16); } catch { osFont = null; }
                            if (osFont != null) break;
                        }
                        if (osFont == null)
                        {
                            try { osFont = Font.CreateDynamicFontFromOSFont(names, 16); } catch { }
                        }
                        if (osFont != null)
                        {
                            _cyrillicFont = TMP_FontAsset.CreateFontAsset(osFont);
                            // Гарантируем наличие кириллических глифов в атласе
                            if (_cyrillicFont != null)
                            {
                                if (_cyrillicFont.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                                {
                                    _cyrillicFont.TryAddCharacters("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя0123456789.:/+-%()");
                                }
                                _cyrillicFont.isMultiAtlasTexturesEnabled = true;
                            }
                        }
                    }
                    catch { }
                }
                return _cyrillicFont;
            }
        }

        // Масштаб размера шрифта для переведённого текста: уменьшаем на 25%.
        private const float SIZE_SCALE = 0.75f;
        // Запоминаем базовый (оригинальный) размер каждого компонента, чтобы не уменьшать повторно.
        private static readonly Dictionary<int, float> _baseTextSize = new Dictionary<int, float>();

        internal static void ApplySizeScale(UnityEngine.UI.Text t)
        {
            if (t == null) return;
            int id = t.GetInstanceID();
            float cur = t.fontSize;
            float baseSize;
            if (_baseTextSize.TryGetValue(id, out baseSize))
            {
                float target = baseSize * SIZE_SCALE;
                // Уже уменьшено — ничего не делаем.
                if (Mathf.Abs(cur - target) <= 0.5f) return;
                // Игра сбросила размер к оригиналу — применяем масштаб.
                if (Mathf.Abs(cur - baseSize) <= 0.5f)
                {
                    t.fontSize = (int)Mathf.Round(target);
                    return;
                }
                // Игра задала новый размер (например, другая карточка в пуле):
                // принимаем его за новый базовый и уменьшаем.
                _baseTextSize[id] = cur;
                t.fontSize = (int)Mathf.Round(cur * SIZE_SCALE);
                return;
            }
            // Первый замер: считаем текущий размер оригинальным.
            _baseTextSize[id] = cur;
            t.fontSize = (int)Mathf.Round(cur * SIZE_SCALE);
        }

        // Запоминаем ОРИГИНАЛЬНЫЙ (английский) текст каждого компонента, чтобы
        // при выключении перевода возвращать его обратно (а не оставлять русский).
        private static readonly Dictionary<int, string> _origById = new Dictionary<int, string>();

        // Вызывается при переводе: сохраняем оригинал, если это новый английский текст.
        internal static void RememberOriginal(UnityEngine.UI.Text t, string english)
        {
            if (t == null || string.IsNullOrEmpty(english)) return;
            int id = t.GetInstanceID();
            if (HasNoLatin(english)) return; // не английский — не запоминаем
            if (HasCyrillic(english)) return; // уже русский — не запоминаем
            if (!_origById.ContainsKey(id))
                _origById[id] = english;
        }

        // Вернуть оригинальный английский текст для конкретного UI.Text (если он сохранён).
        internal static bool RestoreOriginalFor(UnityEngine.UI.Text t)
        {
            if (t == null) return false;
            string orig;
            if (_origById.TryGetValue(t.GetInstanceID(), out orig))
            {
                if (HasCyrillic(t.text))
                {
                    t.text = orig;
                    return true;
                }
            }
            return false;
        }

        // Запомнить оригинал для TMP_Text.
        internal static void RememberOriginalTMP(TMP_Text t, string english)
        {
            if (t == null || string.IsNullOrEmpty(english)) return;
            int id = t.GetInstanceID();
            if (HasNoLatin(english) || HasCyrillic(english)) return;
            if (!_origById.ContainsKey(id))
                _origById[id] = english;
        }

        // Вернуть оригинальный английский текст для конкретного TMP_Text (если он сохранён).
        internal static bool RestoreOriginalForTMP(TMP_Text t)
        {
            if (t == null) return false;
            string orig;
            if (_origById.TryGetValue(t.GetInstanceID(), out orig))
            {
                if (HasCyrillic(t.text))
                {
                    t.text = orig;
                    return true;
                }
            }
            return false;
        }

        // Включить/выключить перевод сразу на всех видимых компонентах.
        public static void SetTranslationEnabled(bool on)
        {
            TranslationEnabled = on;
            SaveTranslationSetting();
            try
            {
                var texts = UnityEngine.UI.Text.FindObjectsOfType<UnityEngine.UI.Text>();
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null) continue;
                    int id = t.GetInstanceID();
                    if (on)
                    {
                        // Включаем: переводим текущий текст (если ещё английский) и запоминаем оригинал.
                        if (!HasNoLatin(t.text))
                        {
                            RememberOriginal(t, t.text);
                            string tr = Translate(t.text);
                            if (tr != t.text)
                            {
                                ApplySizeScale(t);
                                t.text = tr;
                            }
                        }
                    }
                    else
                    {
                        // Выключаем: возвращаем оригинальный английский текст.
                        string orig;
                        if (_origById.TryGetValue(id, out orig) && HasCyrillic(t.text))
                            t.text = orig;
                    }
                }
                var tmps = TMP_Text.FindObjectsOfType<TMP_Text>();
                for (int i = 0; i < tmps.Length; i++)
                {
                    var tmp = tmps[i];
                    if (tmp == null) continue;
                    int id = tmp.GetInstanceID();
                    if (on)
                    {
                        if (!HasNoLatin(tmp.text))
                        {
                            string tr = Translate(tmp.text);
                            if (tr != tmp.text)
                            {
                                if (CyrillicFontAsset != null && tmp.font != CyrillicFontAsset)
                                    tmp.font = CyrillicFontAsset;
                                tmp.text = tr;
                            }
                        }
                    }
                    else
                    {
                        string orig;
                        if (_origById.TryGetValue(id, out orig) && HasCyrillic(tmp.text))
                            tmp.text = orig;
                    }
                }
            }
            catch { }
        }

        public static bool HasCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= 0x0410 && s[i] <= 0x044F) return true;
            }
            return false;
        }

        // Строка не содержит латиницы (только кириллица/цифры/знаки) => переводить не нужно.
        // Это даёт быстрый выход и снимает нагрузку на лагах при волнах мобов.
        public static bool HasNoLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return false;
            }
            return true;
        }

        // Кэш переводов: одни и те же строки игры повторяются (имена, HP, перки),
        // поэтому результат первого перевода переиспользуется — это убирает лаги.
        private static readonly Dictionary<string, string> _transCache = new Dictionary<string, string>(StringComparer.Ordinal);

        // Версия мода (отображается в игре и в установщике).
        public const string ModVersion = "1.0";
        // Перевод включён по умолчанию при запуске игры. Переключается кнопкой в игре.
        public static bool TranslationEnabled = true;

        public static string Translate(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Если перевод выключен кнопкой в игре — возвращаем текст как есть.
            if (!TranslationEnabled) return input;
            // Быстрый выход: если нет латиницы — уже русский/цифры, переводить не нужно.
            // Это критично в бою, когда игра десятки раз за кадр ставит текст.
            if (HasNoLatin(input)) return input;
            // Кэш: повторные одинаковые строки не прогоняем через regex.
            string cached;
            if (_transCache.TryGetValue(input, out cached)) return cached;
            string result = TranslateCore(input);
            // Кэшируем только когда реально перевели (иначе строки с динамикой раздуют кэш).
            if (result != input && _transCache.Count < 20000)
                _transCache[input] = result;
            return result;
        }

        // Полные переводы описаний монстров/механизмов (проверяются только для длинного текста).
        private static string TranslateMonsterDescs(string input)
        {
            input = Regex.Replace(input, @"Chopping this tree will currently yield (\d+)g\.[\r\n\s]*And,\s*a (\d+)% chance to drop cards\.?",
                "Вырубка принесет $1 зол. и шанс $2% получить карты.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Flying on their enchanted brooms.*",
                "Летая на заколдованных метлах, ведьмы невероятно опасны. Их защитная магия крепка, а скорость смертоносна при приближении к башне, где их злые прихоти берут верх и они летят на предельной скорости.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Escaping from their long-forgotten tombs.*",
                "Выбравшись из забытых гробниц, мумии медлительны, но очень опасны. Их тела замотаны в окаменевшие и зачарованные бинты, что не только защищает их от урона, но и позволяет им восстанавливаться.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"All computers need maintenance.*",
                "Все компьютеры требуют обслуживания. Однако большинство неполадок решается простыми инструкциями. RTIT когда-то решил автоматизировать это обслуживание, но их система обрела разум и захватила всё. Эти компьютеры вскоре стали роботами-лидерами механических армий.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Armored goblins are the elite.*",
                "Бронированные гоблины — элитные ветераны гоблиньего рода. Они носят немало брони, что даёт им куда большую живучесть в ущерб скорости. В бою они издают боевой клич, укрепляющий сердца всех союзников поблизости.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Beyond the minds of any mortal.*",
                "За пределами разума смертных эти монстры поглощают физическую форму живых существ в своё тело. Ментальная сила поглощённых существ используется для защиты себя и своих союзников от ядов и восстановления их мистической защиты.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Blink and they are gone\. Orbs.*",
                "Мгновение — и они исчезли. Сферы — странное необъяснимое явление. Одни говорят, что это проявления пойманных духов, другие — что это мерцающая тень существа из четвёртого измерения. Однако известно одно: они опасны.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Covered in wet hides and constructed of strong wood.*",
                "Покрытые мокрыми шкурами и собранные из крепкого дерева, тараны представляют большую угрозу башне. Они медлительны, но чрезвычайно хорошо бронированы. Если таран достигнет башни целым, она получит огромный урон. При разрушении тарана его экипаж бросит его и побежит к башне.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Cranial Carnivores are extremely dangerous.*",
                "Черепные хищники — чрезвычайно опасные существа из другого мира. Их разум обладает невероятной псионической силой, позволяющей их магической защите и даже телу восстанавливаться с тревожной скоростью. Они обладают ненасытной жаждой.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Demons are the soldiers of hell.*",
                "Демоны — солдаты ада. Более живучие, чем бесы, они представляют большую угрозу башне. Их дьявольская зрелость делает их неестественно спокойными перед лицом смерти, а природная склонность к бою — отличными вождями для всех порождений ада.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Enemies' hit points come in the form.*",
                "Очки здоровья врагов делятся на здоровье, броню и щит. Щит врага должен быть исчерпан, прежде чем враг получит урон по броне, а броня — прежде чем получит урон по здоровью. Башни наносят разный урон по разным типам.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Enemies can fortify and haste themselves.*",
                "Враги могут укреплять и ускорять себя и союзников, а ваши башни могут их замедлять. Ускоренные и замедленные враги двигаются быстрее или медленнее в зависимости от силы эффекта. Укреплённые враги получают -5 базового урона, пока действует укрепление.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Even the slow advance of time (?:we call death|that we call death).*",
                "Даже медленное наступление смерти не одолевает эти омерзительные живые трупы. Зомби медлительны, но невероятно живучи. Их тела оживлены странной магией, что делает их уничтожение куда более трудным, чем простое избавление от телесной оболочки.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Every level there is between a \d+% to \d+% chance.*",
                "Каждый уровень есть шанс от 0% до 33%, что башня починит 1 ед. урона. Этот шанс зависит от уровня.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Every time a cannonball hits an enemy there is a chance.*",
                "Каждый раз при попадании ядра во врага есть шанс, что оно разделится на 2 ядра. Каждое несёт в себе урон.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Fires a bolt every \d+ seconds.*",
                "Выпускает болт каждые 3 секунды, нанося урон одному врагу. Баллиста дешева и эффективна.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"From the stars, these aliens have come.*",
                "С далёких звёзд эти пришельцы прибыли ради завоевания. Их передовые материалы делают всё, что захватчики строят и носят, чрезвычайно прочным и хорошо бронированным. Не обманывайтесь их крошечным размером и писклявым лепетом.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Ghouls are foul creatures.*",
                "Упыри — гнусные существа, обитающие в глубоких склепах. Их вечная нежизнь оставляет оболочку мёртвой кожи вокруг их костлявого тела, делая их очень устойчивыми к урону. Благодаря способности чуять запах длинными языками.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Goblins are weak and basic enemies.*",
                "Гоблины — слабые и простые враги. Они представляют очень малую угрозу, но могут стать почти опасными в больших количествах.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"With similar resistances to that of a zombie.*",
                "С защитой, схожей с зомби, эти нежити-чудовища двигаются быстрее своих плотских собратьев. Их тела из костей так же трудно уничтожить, как и тяжёлую броню.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Bats are more of a nuisance.*",
                "Летучие мыши больше досаждают, чем угрожают. Однако их ядовитые укусы делают их опасными для башни.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Flying with the use of technology so advanced.*",
                "Летая на технологиях, неотличимых от магии, эти корабли представляют большую угрозу. Они могут стрелять ракетами в ответ на опасность, а внутри каждого сидит целый танк, готовый к высадке. Единственное объяснение — эти корабли больше внутри.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"These computers soon became.*",
                "Эти компьютеры вскоре обрели разум и захватили всё.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"These elite goblins enter battle.*",
                "Эти элитные гоблины вступают в бой с боевым кличем.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"with their ability to repair.*",
                "с их способностью чинить себя.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Cranial Carnivores have such an insatiable.*",
                "Черепные хищники обладают ненасытной жаждой.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"not only protects the mummies.*",
                "не только защищает мумий от урона, но и позволяет им восстанавливаться.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Monstrous and magical, these demonic.*",
                "Чудовищные и магические, эти демонические крылатые львы — существа истинного разрушения. С нескончаемой злобной ухмылкой эти монстры больше всего любят убивать.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Observers possess similarly strong defensive.*",
                "Наблюдатели обладают столь же сильной защитой, как и черепные хищники. Под угрозой они призывают странные энергии из пустоты, чтобы даровать себе и союзникам защиту из-за пределов нашего мира.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"On haunted nights, it is not only children.*",
                "В ночи полнолуния не только дети выходят на колядки. Эти зачарованные тыквы ищут не конфеты, а лишь убийства. Их толстая тыквенная броня усилена конфетами мёртвых колядовщиков.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Orcs are the older (?:tougher )?brother to goblins.*",
                "Орки — старшие и более крепкие братья гоблинов. Они сильнее и быстрее. Орк может очень быстро бежать, но лишь недолго. Приблизившись к башне, он издаёт боевой клич и ускоряет всех вокруг.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Originally from a strange island in the sea, cyclopses.*",
                "Родом со странного морского острова, циклопы — одни из сильнейших живых существ в этом мире.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Some spiritual beings possess so many corporeal.*",
                "Некоторые духовные существа обладают столь многими телесными качествами, что их почти невозможно отличить от живых.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Some who die in battle reach a warrior's afterlife.*",
                "Некоторые, погибшие в бою, попадают в загробный мир воинов. Однако те, кто умирает, бежав с поля боя, обречены.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Temptresses and murderers, succubi.*",
                "Соблазнительницы и убийцы, суккубы по-настоящему опасны. Под угрозой они телепортируются на большие расстояния, что делает их опасным противником.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Children of the night, vampires stalk forth.*",
                "Дети ночи, вампиры крадутся вперёд. Из-за своей манерности они не носят доспехов. Однако их проклятые силы дают им неестественную живучесть и способность к регенерации. В опасности вампир превращается в летучую мышь.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"The final weapon of the invaders.*",
                "Последнее оружие захватчиков. Это четвероногое создание создано для полного уничтожения. Благодаря технологии «больше внутри», эти ходячие машины смерти могут нести почти бесконечное число ракет.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"These undead monstrosities lumber.*",
                "Эти нежити-чудовища неуклюже двигаются вперёд.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"As the moon fills, so too does the night.*",
                "Когда луна полнеет, ночь наполняется воем волков. Однако эти волки — не обычные звери.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Imps are (?:small|smaller) hellspawn from deep.*",
                "Бесы — мелкие порождения ада из глубин преисподней. При призыве они бегут вперёд, поощряя всех окружающих предаться их кровожадным желаниям. Они не так живучи, как другие адские существа, но умеют регенерировать.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Kill ?bots? are the result of vile machinations.*",
                "Боты-убийцы — результат гнусных махинаций жестоких умов из преисподней. Они созданы для единственной задачи: убивать. Эти бронированные роботы опаснее всего в радиусе ракет. Приблизившись, они выпускают пару очень быстрых ракет по башне. Не отвлекайтесь на их французский акцент, иначе рискуете получить ракетой.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Lacking any physical form, spirits.*",
                "Лишённые физической формы, духи быстры и их трудно убить обычными атаками. Когда нити магии, удерживающие их в этом мире, оказываются под угрозой, они пугаются и несутся к башне.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Launches sawblades to rip and tear.*",
                "Запускает пильные диски, которые режут и рвут врагов, заставляя их кровоточить по мере продвижения по пути.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Lobs an explosive shell over great distances.*",
                "Запускает взрывной снаряд на большие расстояния. Медленный, но отличный против брони. Слабый против щитов.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"If kill ?bots? are the mechanized infantry.*",
                "Если боты-убийцы — механизированная пехота, то эти создания — бронированный таран. Танк-боты построены с дополнительной бронёй и вооружением. С удвоенным вооружением они открывают огонь ракетами у башни и даже когда их внешняя броня пробита, они делают отчаянный залп.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"The strongest and most brutal orcs.*",
                "Самые сильные и жестокие орки заковывают себя в броню, которую могут выдержать. Это делает их куда выносливее обычного орка. В ущерб скорости они берегут силы, чтобы пережить последний рывок атаки. При виде башни они издают боевой клич, укрепляющий всех вокруг.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"These horrible creatures seem to be able to continue.*",
                "Эти ужасные создания, похоже, существуют благодаря чистому умозаключению. Раз они мыслят — они существуют. Они периодически совершают огромные скачки в логике, телепортируя свой аргумент вперёд.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"This strange creation is understood poorly.*",
                "Это странное создание плохо изучено. Хотя оно не существо, оно обладает некоторыми его качествами: способностью чинить и восстанавливать себя и свою защиту. Оно, похоже, обеспечивает прямую связь между нашим миром и другим.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"When a powerful wizard pursues the continuation.*",
                "Когда могущественный волшебник стремится сохранить силу даже после смерти, часто появляется лич. Бессмертный и обладающий магией, лич ведёт нежить, как пастух овец.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"With egos greater than the number of their sides.*",
                "С эго больше числа своих граней икосаэдры — самые заносчивые из всех тел. Если с ними заговорить, они будут хвастаться тем, на скольких стримах они мелькали.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"With nothing else better to do, these solids.*",
                "Этим телам больше нечем заняться, кроме жалоб на десятичную систему, и они утверждают, что двенадцатеричная была бы куда лучше. И хотя они правы, их никто не слушает.", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            input = Regex.Replace(input, @"The most practical of shapes to some, the hexahedron.*",
                "Наиболее практичная форма — гексаэдр, строительный блок всех блоков.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"The tetrahedon is the fundemental geometric.*",
                "Тетраэдр — фундаментальный геометрический строительный блок всего сущего.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"This building can be placed next to iron veins.*",
                "Это здание можно разместить рядом с железными жилами. Оно даёт +1 к макс. здоровью башни и 10% шанс починить урон.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"This building increases your maximum mana.*",
                "Это здание увеличивает макс. запас маны на +20 и создаёт 1 ману/сек за счёт магии рынка.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"This building uses your tower's maximum health.*",
                "Это здание использует макс. здоровье башни как рычаг, чтобы общаться с духами из соседних могил.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Trolls are very large and slow.*",
                "Тролли очень большие и медлительные. У них немало «объёма», который нужно срезать, что делает их сложными для быстрого уничтожения.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Uses mana to engulf enemies with fire.*",
                "Использует ману, чтобы охватить врагов огнём, нанося урон со временем. Отлично против брони, слабо против щитов.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Uses mana to create a concentrated beam.*",
                "Использует ману, чтобы создать концентрированный луч разрушения. Короткая дальность, но отлично разрушает броню.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Uses mana to obliterate single enemies.*",
                "Использует ману, чтобы уничтожать одиночных врагов на предельной дальности.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Burning enemies burn faster based on how much slow.*",
                "Горящие враги горят быстрее в зависимости от силы замедления. Это масштабирует урон горения.", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            input = Regex.Replace(input, @"Clad in a strange, composite armor.*",
                "В странной композитной броне эти танки ведут огромные залпы продвинутого вооружения. При уничтожении экипаж из двух человек покинет его и продолжит бой пешком.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Created for the purpose of destruction, fire elementals.*",
                "Созданные ради разрушения, огненные элементали только жгут. Неудивительно, что их внутреннее «я» — огонь.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Uses mana to spray enemies with poison.*",
                "Использует ману, чтобы распылять на врагов яд, нанося урон со временем. Отлично против щитов, слабо против брони.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"When placed next to an occult shrine this building.*",
                "При размещении рядом с оккультным святилищем это здание позволяет исследовать утраченные знания, давая глобальные бонусы.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"When the hounds of hell are released.*",
                "Когда адские гончие выпущены, мало кто может спастись. С адской живучестью и тайной способностью телепортироваться они будут преследовать вас, куда бы вы ни побежали.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"With a more powerful and vile creation, shadows.*",
                "С более мощным и гнусным творением тени крадутся вперёд, не ведая страха. Они парят медленнее духов и не совершают рывков, однако их слепая жажда убийства лишь крепнет при получении урона.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"Onced used for delivering goods.*",
                "Некогда использовавшиеся для доставки товаров, техноманты превратили эти дружелюбные дроны в доставщиков смерти. Со скоростью «однодневной доставки» они доставят бота-убийцу куда угодно.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"These tanks fire emense salvos.*",
                "Эти танки ведут огромные залпы.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"The children of the night, vampires stalk forth.*",
                "Дети ночи, вампиры крадутся вперёд. Из-за своей манерности они не носят доспехов. Однако их проклятые силы дают им неестественную живучесть и способность к регенерации. В опасности вампир превращается в летучую мышь.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return input;
        }



        private static string TranslateCore(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // ВАЖНО: сначала точный словарный поиск, ДО regex-замен.
            // Причина: regex (TranslateTowerTypes/TranslatePerk) ломают составные ключи
            // вида "Tower - Particle Cannon", после чего словарь их не находит.
            string clean0 = input.Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace('\u00A0', ' ');
            string val0;
            if (Translations.TryGetValue(clean0, out val0)) return val0;
            string norm0 = Regex.Replace(clean0, @"\s+", " ");
            if (Translations.TryGetValue(norm0, out val0)) return val0;

            // === Описания монстров и длинные строки: выполняются ТОЛЬКО для длинного текста ===
            if (input.Length > 40)
            {
                input = TranslateMonsterDescs(input);
            }
            input = Regex.Replace(input, @"Bleeding enemies take an (?:extra|additional) \+(\d+).*?from all attacks\.?",
                "Кровоточащие враги получают дополнительно +$1 к урону здоровью от всех атак.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Burning enemies take an (?:extra|additional) \+(\d+).*?from all attacks\.?",
                "Горящие враги получают дополнительно +$1 к урону броне от всех атак.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Poisoned enemies take an (?:extra|additional) \+(\d+).*?from all attacks\.?",
                "Отравленные враги получают дополнительно +$1 к урону щитам от всех атак.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Attacks against bleeding enemies (?:have|gain) \+?(\d+)%.*",
                "Атаки по кровоточащим врагам получают +$1% к шансу крит. удара.", RegexOptions.IgnoreCase);

            input = Regex.Replace(input, @"All ([a-zA-Z\s\-]+) (?:gain|deal) \+?(\d+)%.*?(?:bleed|кровотеч).*",
                "Все $1: +$2% кровотечение", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"All ([a-zA-Z\s\-]+) (?:gain|deal) \+?(\d+)%.*?(?:burn|горен).*",
                "Все $1: +$2% горение", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"All ([a-zA-Z\s\-]+) (?:gain|deal) \+?(\d+)%.*?(?:poison|яд).*",
                "Все $1: +$2% яд", RegexOptions.IgnoreCase);

            input = Regex.Replace(input, @"\bLevel:\s*(\d+)", "Уровень: $1", RegexOptions.IgnoreCase);
            // HUD: счёт, золото, мана, башня, опыт
            input = Regex.Replace(input, @"\bScore:\s*([\d\.]+)", "Счёт: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bGold:\s*([\d\.]+k?)\s*g?", "Золото: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bMana:\s*([\d\.]+)\/([\d\.]+)\s*\(\+([\d\.]+)\/s\)", "Мана: $1/$2 (+$3/с)", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bTower:\s*(\d+)\/(\d+)", "Башня: $1/$2", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\b(\d+)\s*xp\b", "$1 оп.", RegexOptions.IgnoreCase);
            // Заголовки таблиц статистики (в начале строки многострочной строки)
            input = Regex.Replace(input, @"(?m)^(Gold Generated|Income Source|Income Share|Tower Damage \(this level\)|Dmg/g|Total|Health|Armor|Shield)$",
                M =>
                {
                    switch (M.Groups[1].Value)
                    {
                        case "Gold Generated": return "Золото добыто";
                        case "Income Source": return "Источник дохода";
                        case "Income Share": return "Доля дохода";
                        case "Tower Damage (this level)": return "Урон башен (уровень)";
                        case "Dmg/g": return "Урон/з";
                        case "Total": return "Всего";
                        case "Health": return "Здоровье";
                        case "Armor": return "Броня";
                        case "Shield": return "Щит";
                        default: return M.Groups[1].Value;
                    }
                },
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            input = Regex.Replace(input, @"\bSpeed:\s*", "Скорость: ", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bHealth:\s*", "Здоровье: ", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bArmor:\s*", "Броня: ", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bShield:\s*", "Щит: ", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"This house is protected by (\d+) towers?\.", "Этот дом защищен башнями: $1.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Its next gift will be (\d+)g\.?", "Следующий подарок: $1 зол.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Net gold gifted:\s*(\d+)g\.?", "Всего получено: $1 зол.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"(\d+)\s*HP\.?", "$1 ОЗ", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Defended\s+(\d+)\s+levels?", "Отражено уровней: $1", RegexOptions.IgnoreCase);
            // Одиночные технические подписи
            input = Regex.Replace(input, @"\bDemolish\s*\((\d+g?)\)", "Снести ($1)", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bMana Use:\s*([\d\.]+)/shot", "Расход маны: $1/выстр.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bBase Damage:\s*([\d\.]+)", "Базовый урон: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"(Armor|Health|Shield) Multiplier:\s*([\d\.]+)\s*\(([\d\.]+)\)",
                M => M.Groups[1].Value == "Armor" ? "Множитель брони: " + M.Groups[2].Value + " (" + M.Groups[3].Value + ")"
                   : M.Groups[1].Value == "Health" ? "Множитель здоровья: " + M.Groups[2].Value + " (" + M.Groups[3].Value + ")"
                   : "Множитель щита: " + M.Groups[2].Value + " (" + M.Groups[3].Value + ")", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bRange:\s*([\d\.]+)", "Дальность: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bFire Rate:\s*([\d\.]+)\s*RPM\b", "Скорострельность: $1 об/мин", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bDmg/g\b", "Урон/з", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bProgress\b", "Прогресс", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bFORTIFIED\b", "УКРЕПЛЕНО", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bHASTED\b", "УСКОРЕНО", RegexOptions.IgnoreCase);

            // === НОВЫЕ УНИВЕРСАЛЬНЫЕ ПАТТЕРНЫ (раздел 4 аудита) ===
            // Перки башен: "All X gain/deal +N% damage to shields/armor | bleed/burn/poison"
            input = Regex.Replace(input, @"All ([\w\s\-]+) (?:gain|deal) \+?(\d+%?) (?:damage to (shields|armor)|bleed|burn|poison)",
                M => "Все " + M.Groups[1].Value.Trim() + ": +" + M.Groups[2].Value +
                    (string.IsNullOrEmpty(M.Groups[3].Value) ? (M.Groups[2].Value.EndsWith("%") ? "" : "") : " к урону " + M.Groups[3].Value), RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Increase maximum (bleed|burn)/sec by \+?(\d+) to (\d+)\.?",
                "Увеличивает макс. урон эффекта ($1/с) на +$2 до $3.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Increase main tower's max health by \+?(\d+)",
                "Увеличивает макс. здоровье главной башни на +$1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Current Record[\r\n\s]+Level (\d+)",
                "Текущий рекорд\nУровень $1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            input = Regex.Replace(input, @"(\d+) <NL> \(\+1\)", "$1 (+1)", RegexOptions.IgnoreCase);
            // Позиционный перевод списка TowerTypes (при переносах строк)
            input = TranslateTowerTypes(input);
            // Перевод перков башен ("All X deal an extra N% burn damage...") целиком
            input = TranslatePerk(input);

            // Справка D.O.T. (помогает против разных опечаток "effecctive/effeective" в сборках)
            input = Regex.Replace(input, @"The D\.O\.T\. icons display if.*",
                "Иконки D.O.T. показывают кровотечение, горение или яд на враге и накопленный урон. Урон идёт по своей шкале: кровь — здоровье, огонь — броня, яд — щиты; по чужой шкале — вдвое. Горящие/кровоточащие/отравленные враги получают +1 к своему урону.",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Таблица Game Over: колонки хранятся как МНОГОСТРОЧНЫЕ строки
            // (например "Health\n12K (100%)\n0K..."). Переводим АНКЕРНЫЕ строки-ключи из словаря.
            input = Regex.Replace(input, @"(?m)^(Total|Health|Armor|Shield|Cost|Dmg/g|Gold Generated|Income Source|Income Share|Tower Damage \(this level\)|Tower Damage \(total\)|Monster|House Protection|Death Tax|Banditry / Gold Rush!|Deforestation|Gold Lost)\s*$",
                new MatchEvaluator(TableRowEvaluator),
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            // Опыт "50 \n (+1)" (реальный перенос строки), короткая рубка дерева
            input = Regex.Replace(input, @"(\d+)\s*\r?\n\s*\(\+1\)", "$1 (+1)", RegexOptions.IgnoreCase);
            // HUD-статы башни: проценты "50% Armor" -> "50% Броня"
            input = Regex.Replace(input, @"%\s*(Armor)\b", "% Броня", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Bleed)\b", "% Кровотеч.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Burn)\b", "% Горение", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Health)\b", "% Здоровье", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Poison)\b", "% Яд", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Shield)\b", "% Щит", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"%\s*(Slow)\b", "% Замедление", RegexOptions.IgnoreCase);
            // "+25% Bleed/Burn/Poison"
            input = Regex.Replace(input, @"\+?(\d+)%\s*(Bleed)\b", "$1% кровотеч.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\+?(\d+)%\s*(Burn)\b", "$1% горение", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\+?(\d+)%\s*(Poison)\b", "$1% яд", RegexOptions.IgnoreCase);
            // "+1 AD / +1 HD / +1 SD" — урон броне/здоровью/щитам
            input = Regex.Replace(input, @"\+?(\d+)\s*AD\b", "$1 к урону броне", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\+?(\d+)\s*HD\b", "$1 к урону здоровью", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\+?(\d+)\s*SD\b", "$1 к урону щитов", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Chopping this tree will currently yield (\d+)g\.", "Вырубка принесет $1 зол.", RegexOptions.IgnoreCase);
            // Одиночные значения золота "160g", "100g" и т.п.
            input = Regex.Replace(input, @"(?m)^(\d+)g\s*$", "$1 зол.", RegexOptions.IgnoreCase);
            // Подсказки клавиш (в составе многострочной подсказки сверху)
            input = Regex.Replace(input, @"P or Esc: Pause game", "P или Esc: Пауза", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Hold shift: Smooth cam & fast tower placement", "Shift: плавная камера и быстрая установка", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"H: Hide UI", "H: скрыть интерфейс", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"C: Recenter camera", "C: центрировать камеру", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"T: Show damage & tower stats", "T: урон и характеристики башен", RegexOptions.IgnoreCase);
            // Письмо разработчика A.R.Mason (интро)
            input = Regex.Replace(input, @"Dear Player,\s*It means the world to me.*?A\.R\.Mason",
                "Дорогой игрок,\n\nДля меня много значит, что\nты решил поиграть в эту маленькую\nигру — она изменила мою жизнь.\nЯ стремлюсь создавать вещи, которые\nприносят людям радость. После целой\nжизни неудач мне тепло на душе от того,\nчто ты играешь в неё. Надеюсь, она принесла\nв твою жизнь немного счастья — так же,\nкак твоя игра принесла счастье мне.\n\nСпасибо,\nA.R.Mason",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);


            string clean = input.Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace('\u00A0', ' ');
            string val;
            if (Translations.TryGetValue(clean, out val)) return val;

            string norm = Regex.Replace(clean, @"\s+", " ");
            if (Translations.TryGetValue(norm, out val)) return val;

            foreach (var kv in PrefixTranslations)
            {
                if (input.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = input.Substring(kv.Key.Length);
                    if (!HasCyrillic(suffix)) return kv.Value + suffix;
                }
            }

            // Универсальная нормализация префиксов: "Tower - <Название>" / "Building - ..." и т.п.
            // Если точного ключа с префиксом нет в словаре, переводим хвост по словарю и
            // собираем результат с русским заголовком. Так закрываются все комбинации без
            // ручного добавления каждой в translations.json.
            string[] catPrefixes = new string[] { "Tower", "Building", "Upgrade", "Card", "Monster", "Trait", "Weapon", "Altar", "Rune" };
            string[] catRu = new string[] { "Башня", "Здание", "Улучшение", "Карта", "Монстр", "Свойство", "Оружие", "Алтарь", "Руна" };
            for (int i = 0; i < catPrefixes.Length; i++)
            {
                string head = catPrefixes[i] + " - ";
                if (input.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                {
                    string tail = input.Substring(head.Length).Trim();
                    if (tail.Length >= 2 && !HasCyrillic(tail))
                    {
                        string tailRu = null;
                        if (Translations.TryGetValue(tail, out tailRu) && tailRu != tail)
                            return catRu[i] + ": " + tailRu;
                        // хвост с вариациями
                        string tailNorm = Regex.Replace(tail, @"\s+", " ");
                        if (Translations.TryGetValue(tailNorm, out tailRu) && tailRu != tailNorm)
                            return catRu[i] + ": " + tailRu;
                    }
                }
            }
            return input;
        }

        private static readonly Dictionary<string, string> TowerNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ballista", "Баллиста" }, { "Mortar", "Мортира" }, { "Tesla Coil", "Катушка Тесла" },
            { "Frost Keep", "Ледяная цитадель" }, { "Flame Thrower", "Огнемет" }, { "Poison Sprayer", "Распылитель яда" },
            { "Shredder", "Шредер" }, { "Encampment", "Лагерь" }, { "Lookout", "Дозорная вышка" },
            { "Vampire Lair", "Логово вампиров" }, { "Cannon", "Пушка" }, { "Monument", "Монумент" },
            { "Radar", "Радар" }, { "Obelisk", "Обелиск" }, { "Particle Cannon", "Пушка частиц" },
            { "D.O.T.", "Период. урон" }, { "TowerTypes", "Типы башен" }
        };

        private static string TableRowEvaluator(Match m)
        {
            string v;
            if (Translations.TryGetValue(m.Groups[1].Value, out v)) return v;
            return m.Groups[1].Value;
        }

        private static string TranslatePerk(string input)
        {
            // Перки башен: "All ballistas deal an extra 25% burn damage. Burn damage deals damage
            // over time which is particularly effective against armor. Burning enemies also take
            // additional damage to armor." Нужно перевести название башни + хвост целиком.
            if (HasCyrillic(input) && input.IndexOf("damage", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("gain", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("armor", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("health", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("shields", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("bleed", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("burn", StringComparison.OrdinalIgnoreCase) < 0
                && input.IndexOf("poison", StringComparison.OrdinalIgnoreCase) < 0)
                return input;

            string[] towerPlural = new string[] { "ballistas", "bi-planes", "cannons", "flame throwers",
                "frost keeps", "landmines", "mortars", "obelisks", "particle cannons", "poison sprayers",
                "shredders", "tesla coils", "vampire lairs", "ballista", "bi-plane", "cannon", "flame thrower",
                "frost keep", "landmine", "mortar", "obelisk", "particle cannon", "poison sprayer",
                "shredder", "tesla coil", "vampire lair" };
            string[] towerRu = new string[] { "Баллисты", "Бипланы", "Пушки", "Огнеметы", "Ледяные цитадели",
                "Мины", "Мортиры", "Обелиски", "Пушки частиц", "Распылители яда", "Шредеры", "Катушки Теслы",
                "Логова вампиров", "Баллиста", "Биплан", "Пушка", "Огнемет", "Ледяная цитадель", "Мина",
                "Мортира", "Обелиск", "Пушка частиц", "Распылитель яда", "Шредер", "Катушка Теслы",
                "Логово вампиров" };
            for (int i = 0; i < towerPlural.Length; i++)
            {
                if (input.IndexOf(towerPlural[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    input = Regex.Replace(input, Regex.Escape(towerPlural[i]), towerRu[i], RegexOptions.IgnoreCase);
                    break;
                }
            }
            input = TranslatePerkTail(input);
            return input;
        }

        // Перевод названия эффекта: англ/русс -> русское слово (кровотечение/горение/яд)
        private static string PerkEffRu(string eff)
        {
            if (string.IsNullOrEmpty(eff)) return "эффект";
            string e = eff.ToLower();
            if (e.StartsWith("bleed") || e.StartsWith("кров") || e.Contains("кров")) return "кровотечение";
            if (e.StartsWith("burn") || e.StartsWith("гор") || e.Contains("гор")) return "горение";
            return "яд";
        }

        // Перевод типа урона: armor/health/shields -> броне/здоровью/щитам
        private static string PerkTypeRu(string t)
        {
            if (string.IsNullOrEmpty(t)) return "цели";
            string x = t.ToLower();
            if (x.StartsWith("armor")) return "броне";
            if (x.StartsWith("health")) return "здоровью";
            return "щитам";
        }

        private static string TranslatePerkTail(string input)
        {
            // "All X deal an extra/another N% burn damage." (начало перка)
            input = Regex.Replace(input,
                @"All\s+(.+?)\s+deal\s+(?:an\s+extra|another)\s+\+?(\d+%?)\s+(bleed|burn|poison|Кровотеч\.?|Горение\.?|Яд\.?)\s+damage",
                M => "Все " + M.Groups[1].Value.Trim() + ": +" + M.Groups[2].Value + " " + PerkEffRu(M.Groups[3].Value),
                RegexOptions.IgnoreCase);
            // "X damage deals damage over time which is particularly effective against Y."
            input = Regex.Replace(input,
                @"(Bleed|Burn|Poison|Кровотеч\.?|Горение\.?|Яд\.?)\s+damage\s+deals\s+damage\s+over\s+time\s+which\s+is\s+particularly\s+effective\s+against\s+(armor|health|shields)",
                M => "Урон от " + PerkEffRu(M.Groups[1].Value) + " наносится постепенно и особенно эффективен против " + PerkTypeRu(M.Groups[2].Value) + ".",
                RegexOptions.IgnoreCase);
            // "Xing enemies also take additional damage to Y."
            input = Regex.Replace(input,
                @"(Bleeding|Burning|Poisoned|Кровоточащие|Горящие|Отравленные)\s+enemies\s+also\s+take\s+additional\s+damage\s+to\s+(armor|health|shields)",
                M =>
                {
                    string eff = M.Groups[1].Value.ToLower();
                    string adj = eff.StartsWith("bleed") || eff.Contains("кров") ? "Кровоточащие"
                                : eff.StartsWith("burn") || eff.Contains("гор") ? "Горящие" : "Отравленные";
                    return adj + " враги также получают дополнительный урон по " + PerkTypeRu(M.Groups[2].Value) + ".";
                }, RegexOptions.IgnoreCase);
            // "All X gain/deal +N damage to armor/health/shields"
            input = Regex.Replace(input,
                @"All\s+(.+?)\s+(?:gain|deal)\s+\+?(\d+)\s+damage\s+to\s+(armor|health|shields)",
                M => "Все " + M.Groups[1].Value.Trim() + ": +" + M.Groups[2].Value + " к урону по " + PerkTypeRu(M.Groups[3].Value),
                RegexOptions.IgnoreCase);
            // "All X gain +N% bleed/burn/poison"
            input = Regex.Replace(input,
                @"All\s+(.+?)\s+gain\s+\+?(\d+%?)\s+(bleed|burn|poison|Кровотеч\.?|Горение\.?|Яд\.?)",
                M => "Все " + M.Groups[1].Value.Trim() + ": +" + M.Groups[2].Value + " " + PerkEffRu(M.Groups[3].Value),
                RegexOptions.IgnoreCase);
            // "All towers gain crit chance equal to their level but lose N base damage."
            input = Regex.Replace(input,
                @"All\s+towers\s+gain\s+crit\s+chance\s+equal\s+to\s+their\s+level\s+but\s+lose\s+(\d+)\s+base\s+damage",
                M => "Все башни получают шанс крит. удара, равный уровню, но теряют " + M.Groups[1].Value + " базового урона",
                RegexOptions.IgnoreCase);
            // Хвост после перевода названия башни: "Все Баллисты: +1 к урону armor"
            input = Regex.Replace(input, @"\barmor\b", "броне", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bshields\b", "щитам", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bhealth\b(?!\s*[-\d])", "здоровью", RegexOptions.IgnoreCase);
            // "Punch holes in enemies with cannon balls"
            input = Regex.Replace(input, @"Punch\s+holes\s+in\s+enemies\s+with\s+(.+?)\s+balls",
                M => "Пробивает дыры во врагах снарядами (" + M.Groups[1].Value.Trim() + ").", RegexOptions.IgnoreCase);
            // "enemies receive -N base damage from attacks"
            input = Regex.Replace(input, @"enemies\s+receive\s+-(\d+)\s+base\s+damage\s+from\s+attacks",
                M => "враги получают на " + M.Groups[1].Value + " базового урона меньше от атак", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\bfrom\s+attacks\b", "от атак", RegexOptions.IgnoreCase);
            // "All X gain +N% crit chance."
            input = Regex.Replace(input,
                @"All\s+(.+?)\s+gain\s+\+?(\d+%?)\s+crit\s+chance",
                M => "Все " + M.Groups[1].Value.Trim() + ": +" + M.Groups[2].Value + " к шансу крит. удара",
                RegexOptions.IgnoreCase);
            // "All X slow enemies for N% of the damage they deal."
            input = Regex.Replace(input,
                @"All\s+(.+?)\s+slow\s+enemies\s+for\s+(?:an\s+additional\s+)?(\d+%?)\s+of\s+the\s+damage\s+they\s+deal",
                M => "Все " + M.Groups[1].Value.Trim() + " замедляют врагов на " + M.Groups[2].Value + " от наносимого урона",
                RegexOptions.IgnoreCase);
            // Подписи дома (налог на смерть, могилы, доля здоровья башни)
            input = Regex.Replace(input, @"Tax\s+rate:\s*([\d\.]+)%", "Ставка налога: $1%", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Death\s+tax\s+due:\s*([\d\.]+)", "Налог на смерть к оплате: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Net\s+tax\s+collected:\s*([\d\.]+)g", "Чисто собрано налогов: $1 зол.", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"Nearby\s+graves:\s*x(\d+)", "Могил поблизости: $1", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"of\s+(\d+)\s+\(tower\s+health\)", "из $1 (здоровье башни)", RegexOptions.IgnoreCase);
            // "Fortifies nearby paths with landmines"
            input = Regex.Replace(input, @"Fortifies\s+nearby\s+paths\s+with\s+landmines", "Укрепляет ближайшие пути минами", RegexOptions.IgnoreCase);
            // Чистим двойные точки, возникшие при сборке фраз.
            input = Regex.Replace(input, @"\.\s*\.", ".");
            return input;
        }

        private static string TranslateTowerTypes(string input)
        {
            // Переводим названия башен в многострочном списке TowerTypes. Заменяем только
            // точные английские названия (русские не затрагиваем). Ключи длиннее — первыми.
            List<KeyValuePair<string, string>> ordered = new List<KeyValuePair<string, string>>(TowerNameMap);
            ordered.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            string result = input;
            foreach (var kv in ordered)
            {
                if (kv.Value == kv.Key) continue;
                // Заменяем слово целиком (по границам), чтобы не задеть русские/части строк.
                result = Regex.Replace(result, @"\b" + Regex.Escape(kv.Key) + @"\b", kv.Value, RegexOptions.IgnoreCase);
            }
            return result;
        }


        public static void TranslateTMP(TMP_Text tmp)
        {
            if (tmp == null || string.IsNullOrEmpty(tmp.text)) return;
            if (!TranslationEnabled) return; // перевод выключен — не трогаем
            if (HasNoLatin(tmp.text)) return;
            int id = tmp.GetInstanceID();
            // Запоминаем оригинал (английский), чтобы можно было вернуть при выключении.
            if (HasCyrillic(tmp.text) == false && !_origById.ContainsKey(id))
                _origById[id] = tmp.text;
            string trans = Translate(tmp.text);
            if (trans != tmp.text)
            {
                if (CyrillicFontAsset != null && tmp.font != CyrillicFontAsset)
                    tmp.font = CyrillicFontAsset;
                // Текст ТОЛЬКО переводим. Ни размер, ни перенос, ни переполнение не трогаем.
                tmp.text = trans;
            }
        }

        private void Awake()
        {
            PluginDir = Paths.PluginPath;
            string jsonPath = Path.Combine(PluginDir, "translations.json");
            if (File.Exists(jsonPath))
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(jsonPath, System.Text.Encoding.UTF8));
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                    {
                        Translations[kv.Key] = kv.Value;
                        if (kv.Key.EndsWith(": ") && kv.Key.Length > 2)
                            PrefixTranslations[kv.Key] = kv.Value;
                    }
                }
            }
            var harmony = new Harmony("com.rogueTower.russian");
            harmony.PatchAll();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            LoadTranslationSetting();
        }

        // Сохранение настройки перевода (ВКЛ/ВЫКЛ) между запусками игры.
        private static readonly string SettingPath = Path.Combine(PluginDir, "translation_setting.txt");

        public static void SaveTranslationSetting()
        {
            try { File.WriteAllText(SettingPath, TranslationEnabled ? "1" : "0", System.Text.Encoding.UTF8); } catch { }
        }

        public static void LoadTranslationSetting()
        {
            try
            {
                if (File.Exists(SettingPath))
                {
                    string v = File.ReadAllText(SettingPath, System.Text.Encoding.UTF8).Trim();
                    TranslationEnabled = (v != "0"); // по умолчанию ВКЛ
                }
                else
                {
                    TranslationEnabled = true; // первый запуск — перевод включён
                }
            }
            catch { TranslationEnabled = true; }
        }

        private float _timer = 0f;
        private static readonly HashSet<int> _processedIds = new HashSet<int>();

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try { _processedIds.Clear(); } catch { }
            try { _baseTextSize.Clear(); } catch { }
            try { _origById.Clear(); } catch { }
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            // Быстрый резервный обход (каждые 0.5с): подхватывает текст, который не прошёл
            // через setter-патчи (например, задан до их активации). Мгновенно пропускаем
            // уже русское и обработанное, поэтому нагрузка минимальна.
            if (_timer < 0.5f) return;
            _timer = 0f;

            var canvases = FindObjectsOfType<Canvas>();
            for (int c = 0; c < canvases.Length; c++)
            {
                var canvas = canvases[c];
                if (canvas == null) continue;

                var texts = canvas.GetComponentsInChildren<UnityEngine.UI.Text>(false);
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null) continue;
                    int id = t.GetInstanceID();
                    if (_processedIds.Contains(id)) continue;
                    if (!TranslationEnabled) continue; // перевод выключен — не трогаем
                    if (HasNoLatin(t.text)) { _processedIds.Add(id); continue; }
                    RememberOriginal(t, t.text);
                    string trans = Translate(t.text);
                    if (trans != t.text)
                    {
                        ApplySizeScale(t);
                        t.text = trans;
                    }
                    if (!HasNoLatin(t.text)) _processedIds.Add(id);
                }
            }
        }

        private void OnGUI()
        {
            // Панель управления переводом в правом верхнем углу.
            try
            {
                float w = 250f;
                float btnH = 30f;
                float top = 12f;
                float left = Screen.width - w - 14f;

                // Подпись с версией.
                GUI.color = Color.white;
                GUI.Label(new Rect(left, top, w, 22f), "Rogue Tower RU v" + ModVersion);

                // Кнопка ВКЛ/ВЫКЛ перевода (выше подсказок, справа).
                string label = TranslationEnabled ? "Перевод: ВКЛ" : "Перевод: ВЫКЛ";
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = TranslationEnabled ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.6f, 0.2f, 0.2f);
                if (GUI.Button(new Rect(left, top + 24f, w, btnH), label))
                {
                    SetTranslationEnabled(!TranslationEnabled);
                }
                GUI.backgroundColor = old;
                GUI.color = Color.white;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
    public static class Patch_TMP_SetText
    {
        private static void Prefix(TMP_Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value) || __instance == null) return;
            // Если перевод выключен — не переводим (игра сама задаёт английский текст).
            if (!TranslatorPlugin.TranslationEnabled) return;
            // Быстрый выход: нет латиницы => уже русский/цифры, ничего не делаем (снимает лаги).
            if (TranslatorPlugin.HasNoLatin(value)) return;
            string trans = TranslatorPlugin.Translate(value);
            if (trans != value)
            {
                TranslatorPlugin.RememberOriginalTMP(__instance, value);
                if (TranslatorPlugin.CyrillicFontAsset != null && __instance.font != TranslatorPlugin.CyrillicFontAsset)
                    __instance.font = TranslatorPlugin.CyrillicFontAsset;
                value = trans;
            }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "text", MethodType.Setter)]
    public static class Patch_UIText_SetText
    {
        private static void Prefix(UnityEngine.UI.Text __instance, ref string value)
        {
            if (__instance == null || string.IsNullOrEmpty(value)) return;
            // Если перевод выключен — не переводим (игра сама задаёт английский текст).
            if (!TranslatorPlugin.TranslationEnabled) return;
            // Быстрый выход: нет латиницы => уже русский/цифры, ничего не делаем (снимает лаги).
            if (TranslatorPlugin.HasNoLatin(value)) return;
            // Компоненты карточек переиспользуются игрой (пул): при каждой новой
            // записи английского текста переводим заново и запоминаем оригинал.
            TranslatorPlugin.RememberOriginal(__instance, value);
            string trans = TranslatorPlugin.Translate(value);
            if (trans != value)
            {
                // Уменьшаем размер шрифта на 25% (идемпотентно — не уменьшаем повторно).
                TranslatorPlugin.ApplySizeScale(__instance);
                value = trans;
            }
        }
    }
}
