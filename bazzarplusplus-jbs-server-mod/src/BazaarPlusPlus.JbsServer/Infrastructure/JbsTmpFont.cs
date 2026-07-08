#nullable enable
using System;
using System.IO;
using System.Reflection;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace BazaarPlusPlus.JbsServer.Infrastructure;

internal static class JbsTmpFont
{
    private const string LogCategory = "JbsTmpFont";
    private const string FontFileName = "LXGWWenKai-Regular.ttf";
    private const string ResourceName = "BazaarPlusPlus.JbsServer.Resources.Fonts.LXGWWenKai-Regular.ttf";
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9;
    private const int AtlasSize = 2048;

    private static TMP_FontAsset? _fontAsset;
    private static bool _loadFailureLogged;

    internal static bool TryApply(TMP_Text? text, string? sampleText)
    {
        if (text == null)
            return false;

        var fontAsset = ResolveDefault();
        if (fontAsset == null)
            return false;

        text.font = fontAsset;
        if (fontAsset.material != null)
            text.fontSharedMaterial = fontAsset.material;

        WarmCharacters(fontAsset, sampleText);
        return true;
    }

    private static TMP_FontAsset? ResolveDefault()
    {
        if (_fontAsset != null)
            return _fontAsset;

        try
        {
            var fontPath = ExtractFont();
            var font = new Font(fontPath);
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true
            );
            if (fontAsset == null)
            {
                LogLoadFailure("CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = "JBS LXGWWenKai TMP";
            _fontAsset = fontAsset;
            JbsLog.Info(LogCategory, $"Loaded TMP UI font '{FontFileName}'.");
            return _fontAsset;
        }
        catch (Exception ex)
        {
            LogLoadFailure(ex.Message);
            return null;
        }
    }

    private static string ExtractFont()
    {
        var cacheRoot = Path.Combine(GetCacheRoot(), "BazaarPlusPlus", "JbsServer", "Fonts");
        Directory.CreateDirectory(cacheRoot);
        var targetPath = Path.Combine(cacheRoot, FontFileName);

        var assembly = Assembly.GetExecutingAssembly();
        using var resource = assembly.GetManifestResourceStream(ResourceName);
        if (resource == null)
            throw new FileNotFoundException($"Embedded font resource '{ResourceName}' was not found.", ResourceName);

        if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
            return targetPath;

        using var output = File.Create(targetPath);
        resource.CopyTo(output);
        return targetPath;
    }

    private static string GetCacheRoot()
    {
        if (!string.IsNullOrWhiteSpace(Paths.CachePath))
            return Paths.CachePath;

        return Path.Combine(Path.GetTempPath(), "BepInEx", "cache");
    }

    private static void WarmCharacters(TMP_FontAsset fontAsset, string? sampleText)
    {
        if (string.IsNullOrEmpty(sampleText))
            return;

        try
        {
            fontAsset.TryAddCharacters(sampleText, out _);
        }
        catch (Exception ex)
        {
            JbsLog.Debug(LogCategory, $"Failed to warm TMP glyphs: {ex.Message}");
        }
    }

    private static void LogLoadFailure(string reason)
    {
        if (_loadFailureLogged)
            return;

        _loadFailureLogged = true;
        JbsLog.Warn(LogCategory, $"Failed to load embedded TMP UI font. {reason}");
    }
}
