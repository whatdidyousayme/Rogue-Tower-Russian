using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RogueTowerRussian
{
    // No Unity dependency: the same translator is used in the game and coverage checks.
    public sealed class TranslationCatalog
    {
        private readonly Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Regex phrases;
        private static readonly Regex Numbers = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        private static string Normalize(string s) { return Regex.Replace(s.Replace('\u00a0', ' '), @"\s+", " ").Trim(); }
        private static string Skeleton(string s) { return Numbers.Replace(Normalize(s), "#"); }

        public TranslationCatalog(IDictionary<string, string> source)
        {
            foreach (var pair in source)
            {
                string key = Normalize(pair.Key);
                if (key.Length == 0 || String.IsNullOrEmpty(pair.Value)) continue;
                entries[key] = pair.Value;
                string[] en = Numbers.Matches(key).Cast<Match>().Select(m => m.Value).ToArray();
                string[] ru = Numbers.Matches(pair.Value).Cast<Match>().Select(m => m.Value).ToArray();
                if (en.Length > 0 && en.SequenceEqual(ru)) templates[Skeleton(key)] = pair.Value;
            }
            var keys = entries.Keys.Where(k => k.Length >= 2 && k.Length <= 120 && !Numbers.IsMatch(k) && !k.Contains("<"))
                .OrderByDescending(k => k.Length).Select(Regex.Escape).ToArray();
            phrases = new Regex(@"(?<![\p{L}\p{N}_])(?:" + String.Join("|", keys) + @")(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        public string Translate(string input)
        {
            if (String.IsNullOrEmpty(input)) return input;
            string output;
            if (entries.TryGetValue(Normalize(input), out output)) return output;
            // Rich text markup, sprite names and color codes must never be translated.
            if (Regex.IsMatch(input, @"<[^>]+>"))
                return String.Join("", Regex.Split(input, @"(<[^>]+>)").Select(p => p.StartsWith("<") && p.EndsWith(">") ? p : TranslatePlain(p)));
            return TranslatePlain(input);
        }

        private string TranslatePlain(string input)
        {
            string result;
            if (entries.TryGetValue(Normalize(input), out result)) return PreservePadding(input, result);
            string special = Regex.Replace(input, @"Chopping this tree will currently yield (\d+)g\.(?:\s*And, a (\d+)% chance (?:of dropping|to drop) cards\.)?", m =>
                "Вырубка принесёт " + m.Groups[1].Value + " зол." + (m.Groups[2].Success ? "\nШанс получить карты: " + m.Groups[2].Value + "%." : ""), RegexOptions.IgnoreCase);
            special = Regex.Replace(special, @"This house is protected by (\d+) towers?\.", "Количество башен, защищающих дом: $1.", RegexOptions.IgnoreCase);
            special = Regex.Replace(special, @"New (single|double|triple|quadruple) defense record!\s*\+(\d+) bonus xp", m =>
                "Новый рекорд " + new Dictionary<string,string> {{"single","одиночной"},{"double","двойной"},{"triple","тройной"},{"quadruple","четверной"}}[m.Groups[1].Value.ToLowerInvariant()] + " обороны!\n+" + m.Groups[2].Value + " бонусного опыта", RegexOptions.IgnoreCase);
            if (special != input) return special;
            if (templates.TryGetValue(Skeleton(input), out result))
            {
                var values = Numbers.Matches(input).Cast<Match>().Select(m => m.Value).ToArray();
                int index = 0;
                return PreservePadding(input, Numbers.Replace(result, m => values[index++]));
            }
            if (input.Contains("\n"))
                return String.Join("", Regex.Split(input, @"(\r?\n)").Select(p => p.Contains("\n") ? p : TranslatePlain(p)));

            result = phrases.Replace(input, m => entries[m.Value].Trim());
            result = Regex.Replace(result, @"(?<=\d)g\b", " зол.");
            result = Regex.Replace(result, @"/shot\b", "/выстр.", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"/(?:sec|s)\b", "/с", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"/kill\b", "/убийство", RegexOptions.IgnoreCase);
            return result;
        }

        private static string PreservePadding(string original, string translated)
        {
            return Regex.Match(original, @"^\s*").Value + translated.Trim() + Regex.Match(original, @"\s*$").Value;
        }
    }
}
