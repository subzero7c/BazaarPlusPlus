#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BazaarPlusPlus.JBSMatch;

internal static class JbsLocalization
{
    private static string _localeDir = "";
    private static string _loadedLang = "\x00"; // force first load
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    internal static void Initialize(string pluginDir)
    {
        _localeDir = Path.Combine(pluginDir, "JBSMatch", "locales");
        Reload(GetCurrentLanguage());
    }

    internal static string Get(string key, params object[] args)
    {
        var lang = GetCurrentLanguage();
        if (lang != _loadedLang)
            Reload(lang);

        if (_strings.TryGetValue(key, out var template))
            return args.Length > 0 ? string.Format(template, args) : template;

        return key;
    }

    private static string GetCurrentLanguage()
    {
        try { return PlayerPreferences.Data.LanguageCode ?? ""; }
        catch { return ""; }
    }

    private static void Reload(string lang)
    {
        _loadedLang = lang;
        var locale = IsChinese(lang) ? "zh-CN" : "en-US";

        var path = Path.Combine(_localeDir, $"{locale}.json");
        if (File.Exists(path))
        {
            _strings = ParseLocaleJson(File.ReadAllText(path, Encoding.UTF8));
            return;
        }

        if (locale != "zh-CN")
        {
            var fallback = Path.Combine(_localeDir, "zh-CN.json");
            if (File.Exists(fallback))
            {
                _strings = ParseLocaleJson(File.ReadAllText(fallback, Encoding.UTF8));
                return;
            }
        }

        JbsLog.Warn("Localization", $"Locale file not found: {path}");
        _strings = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static bool IsChinese(string lang)
        => !string.IsNullOrEmpty(lang)
            && (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                || lang.Contains("Chinese", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ParseLocaleJson(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;

        // advance to opening brace
        while (i < json.Length && json[i] != '{') i++;
        if (i >= json.Length) return result;
        i++;

        while (i < json.Length)
        {
            // skip whitespace / commas
            while (i < json.Length && (json[i] is ' ' or '\t' or '\n' or '\r' or ',')) i++;
            if (i >= json.Length || json[i] == '}') break;
            if (json[i] != '"') { i++; continue; }

            var key = ReadString(json, ref i);
            if (key == null) break;

            while (i < json.Length && json[i] != ':') i++;
            if (i >= json.Length) break;
            i++; // skip ':'

            while (i < json.Length && (json[i] is ' ' or '\t')) i++;
            if (i >= json.Length || json[i] != '"') break;

            var value = ReadString(json, ref i);
            if (value == null) break;

            result[key] = value;
        }

        return result;
    }

    private static string? ReadString(string json, ref int i)
    {
        if (i >= json.Length || json[i] != '"') return null;
        i++; // skip opening '"'

        var sb = new StringBuilder();
        while (i < json.Length)
        {
            var c = json[i++];
            if (c == '"') break;
            if (c == '\\' && i < json.Length)
            {
                var esc = json[i++];
                switch (esc)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    default:   sb.Append(esc);  break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
