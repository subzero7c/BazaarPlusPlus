#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.Settings.Visual;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed partial class BppSettingsDockController
{
    private void ConfigurePanelRect(RectTransform rectTransform, BppSettingsDockPlacement placement)
    {
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot =
            placement.PanelDirection == BppSettingsDockPanelDirection.UpRight
                ? new Vector2(0f, 0f)
                : new Vector2(1f, 0f);

        var cloneScale = _dockButtonRect != null ? _dockButtonRect.localScale.x : 1f;
        var panelScale = BppSettingsDockGeometry.CalculatePanelLocalScale(
            BppSettingsDockVisualConstants.PanelExpandedScale,
            cloneScale
        );
        rectTransform.localScale = new Vector3(panelScale, panelScale, 1f);
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.anchoredPosition =
            placement.PanelDirection == BppSettingsDockPanelDirection.UpRight
                ? new Vector2(8f, 0f)
                : new Vector2(-8f, 0f);
        rectTransform.sizeDelta = new Vector2(
            BppSettingsDockVisualConstants.PanelWidth,
            BppSettingsDockVisualConstants.CalculatePanelHeight(BppSettingsDockCatalog.Definitions.Count)
        );
    }

    private static void ConfigurePanelVisual(GameObject panelObject)
    {
        var background = panelObject.GetComponent<Image>();
        if (background != null)
        {
            background.color = BppSettingsDockVisualConstants.PanelBackground;
            background.raycastTarget = true;
        }

        var outline = panelObject.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = BppSettingsDockVisualConstants.PanelOutlineColor;
            outline.effectDistance = BppSettingsDockVisualConstants.PanelOutlineDistance;
            outline.useGraphicAlpha = true;
        }
    }

    private static void ConfigureHeaderRect(RectTransform headerRect)
        => BppSettingsDockVisualConstants.ConfigureHeaderRect(headerRect);

    private static void ConfigureRowRect(RectTransform rowRect, int index)
        => BppSettingsDockVisualConstants.ConfigureRowRect(rowRect, index);

    private TextMeshProUGUI? CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color
    )
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        ApplyTextStyle(text);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private void ResolveTextStyle()
    {
        if (_anchorButton == null)
            return;

        TextMeshProUGUI? templateSource = null;
        var template = FindTemplateText(_anchorButton.transform);
        templateSource = template;
        if (template == null)
        {
            var hostRect = _anchorButton.transform.parent;
            if (hostRect != null)
            {
                template = FindTemplateText(hostRect);
                templateSource = template;
            }
        }

        if (template == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (candidate != null && candidate.font != null)
                {
                    template = candidate;
                    templateSource = candidate;
                    break;
                }
            }
        }

        if (template == null)
            return;

        _uiFont = template.font;
        _uiFontMaterial = template.fontSharedMaterial;

        if (!_fontResolutionLogged && _uiFont != null)
        {
            _fontResolutionLogged = true;
            BppLog.Info(
                LogCategory,
                $"Resolved TMP font '{_uiFont.name}' material '{_uiFontMaterial?.name ?? "<null>"}' from '{BuildTransformPath(templateSource?.transform)}' text='{templateSource?.text ?? string.Empty}'."
            );
        }
    }

    private static TextMeshProUGUI? FindTemplateText(Transform root)
    {
        foreach (var candidate in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (candidate != null && candidate.font != null)
                return candidate;
        }

        return null;
    }

    private static string BuildTransformPath(Transform? transform)
    {
        if (transform == null)
            return "<unknown>";

        var segments = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            segments.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", segments);
    }

    private void ApplyTextStyle(TextMeshProUGUI text, string? sampleText = null)
    {
        _uiFont ??= TMP_Settings.defaultFontAsset;
        if (_uiFont != null)
            text.font = _uiFont;

        if (_uiFontMaterial != null)
            text.fontSharedMaterial = _uiFontMaterial;

        BppTmpFont.TryApply(text, sampleText ?? text.text);
        text.richText = false;
    }

    private static string ResolveHeader(string languageCode)
    {
        return "BazaarPlusPlus";
    }
}
