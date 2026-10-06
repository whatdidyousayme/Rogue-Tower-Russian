using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using RogueTowerRussian;
class CheckTranslations {
    static void Main(string[] args) {
        var json = new JavaScriptSerializer();
        var catalog = new TranslationCatalog(json.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[0])));
        var texts = json.Deserialize<List<string>>(File.ReadAllText(args[1]));
        var missing = new Dictionary<string,string>();
        foreach (string text in texts) {
            string ru = catalog.Translate(text);
            string plain = Regex.Replace(ru, @"<[^>]+>|Rogue Tower|\b(?:Esc|Shift|[IVXLCDM]+)\b", "");
            if (Regex.IsMatch(plain, @"[A-Za-z]{2,}")) missing[text] = ru;
        }
        File.WriteAllText(args[2], json.Serialize(missing), Encoding.UTF8);
        Console.WriteLine("Text candidates: " + texts.Count + "; remaining Latin: " + missing.Count);
        string[,] samples = {
            {"Gold: 12345", "Золото: 12345"},
            {"<color=#FFAABB>Ballista</color>", "<color=#FFAABB>Баллиста</color>"},
            {"Current Record\nLevel 57", "Текущий рекорд\nУровень 57"},
            {"Chopping this tree will currently yield 135g. \nAnd, a 27% chance of dropping cards.", "Вырубка принесёт 135 зол.\nШанс получить карты: 27%."},
            {"Chopping this tree will currently yield 41g.\nAnd, a 19% chance to drop cards.", "Вырубка принесёт 41 зол.\nШанс получить карты: 19%."},
            {"All enemies drop an additional 9 gold when they die.", "Дополнительное золото за гибель каждого врага: 9."},
            {"This house is protected by 6 towers.", "Количество башен, защищающих дом: 6."},
            {"This house is protected by 1 towers.\nIts next gift will be 2g.\nNet gold gifted: 17g.", "Количество башен, защищающих дом: 1.\nСледующий подарок: 2 золота.\nВсего подарено: 17 золота."},
            {"Demolish (123g)", "Снести (123 зол.)"},
            {"Mana: 134/250 (+1.5/s)", "Мана: 134/250 (+1.5/с)"},
            {"Defended all 45 levels", "Отражены все 45 уровней"},
            {"New double defense record!\n +27 bonus xp", "Новый рекорд двойной обороны!\n+27 бонусного опыта"},
            {"+3 Base Damage\n+12% Freeze Chance\n(8 + level)% Crit", "+3 к базовому урону\n+12% Шанс заморозки\n(8 + уровень)% Крит. шанс"}
        };
        for (int i=0; i<samples.GetLength(0); i++) {
            string got = catalog.Translate(samples[i,0]);
            if (got != samples[i,1]) throw new Exception(samples[i,0] + " => " + got + " expected " + samples[i,1]);
        }
        Console.WriteLine("Dynamic values and rich text checks passed.");
        const string loading = "Loading...", translatedLoading = "Загрузка...";
        for (int i = 0; i <= loading.Length; i++) {
            string got = TranslationCatalog.TranslateLoadingProgress(loading.Substring(0, i));
            if (!translatedLoading.StartsWith(got) || (i == loading.Length && got != translatedLoading))
                throw new Exception("Loading animation translation failed at " + i);
        }
        Console.WriteLine("Loading animation prefixes passed.");
    }
}
