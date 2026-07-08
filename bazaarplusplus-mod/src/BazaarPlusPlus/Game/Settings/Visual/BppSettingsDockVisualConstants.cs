#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings.Visual;

public static class BppSettingsDockVisualConstants
{
    internal const string HeaderObjectName = "BPP_SettingsDockHeader";
    internal const string BppRowObjectNamePrefix = "BPP_SettingsDockRow_";
    public const string JbsRowObjectNamePrefix = "JBS_SettingsDockRow_";

    internal const float PanelWidth = 456f;
    internal const float PanelExpandedScale = 1.5f;
    internal const float PanelPadding = 18f;
    internal const float PanelTopPadding = 16f;
    internal const float PanelBottomPadding = 28f;
    internal const float HeaderHeight = 24f;
    internal const float HeaderSpacing = 16f;
    internal const float RowHeight = 48f;
    internal const float RowSpacing = 12f;
    internal const float RowInnerPadding = 16f;
    internal const float StatusWidth = 80f;
    internal const float HeaderFontSize = 21f;
    internal const float RowLabelFontSize = 19f;
    internal const float RowStatusFontSize = 17f;

    internal static readonly Color PanelBackground = new(0.09f, 0.09f, 0.11f, 0.96f);
    internal static readonly Color PanelOutlineColor = new(0.76f, 0.45f, 0.14f, 0.75f);
    internal static readonly Vector2 PanelOutlineDistance = new(1.5f, -1.5f);
    internal static readonly Color HeaderTextColor = new(0.97f, 0.83f, 0.49f, 1f);
    internal static readonly Color RowLabelActiveColor = new(0.93f, 0.93f, 0.95f, 1f);
    internal static readonly Color RowLabelDisabledColor = new(0.75f, 0.78f, 0.82f, 0.98f);
    internal static readonly Color RowStatusEnabledColor = new(0.90f, 0.97f, 0.78f, 1f);
    internal static readonly Color RowStatusDisabledColor = new(0.75f, 0.78f, 0.82f, 0.98f);
    internal static readonly Color RowEnabledBackground = new(0.23f, 0.35f, 0.22f, 0.94f);
    internal static readonly Color RowDisabledBackground = new(0.19f, 0.19f, 0.22f, 0.92f);
    internal static readonly Color RowEnabledOutlineColor = new(0.78f, 0.86f, 0.46f, 0.70f);
    internal static readonly Color RowDisabledOutlineColor = new(0f, 0f, 0f, 0.45f);
    internal static readonly Vector2 RowOutlineDistance = new(1f, -1f);

    public static float CalculatePanelHeight(int rowCount)
    {
        var rowsHeight = rowCount > 0
            ? (rowCount * RowHeight) + ((rowCount - 1) * RowSpacing)
            : 0f;
        return PanelTopPadding + HeaderHeight + HeaderSpacing + rowsHeight + PanelBottomPadding;
    }

    internal static void ConfigureHeaderRect(RectTransform headerRect)
    {
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.offsetMin = new Vector2(PanelPadding, -PanelTopPadding - HeaderHeight);
        headerRect.offsetMax = new Vector2(-PanelPadding, -PanelTopPadding);
    }

    public static void ConfigureRowRect(RectTransform rowRect, int index)
    {
        var rowTop = PanelTopPadding + HeaderHeight + HeaderSpacing + (index * (RowHeight + RowSpacing));
        rowRect.offsetMin = new Vector2(PanelPadding, -(rowTop + RowHeight));
        rowRect.offsetMax = new Vector2(-PanelPadding, -rowTop);
    }

    public static void ConfigureLabelRect(RectTransform labelRect)
    {
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.offsetMin = new Vector2(RowInnerPadding, 0f);
        labelRect.offsetMax = new Vector2(-(StatusWidth + RowInnerPadding + 8f), 0f);
    }

    public static void ConfigureStatusRect(RectTransform statusRect)
    {
        statusRect.anchorMin = new Vector2(1f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.sizeDelta = new Vector2(StatusWidth, RowHeight);
        statusRect.anchoredPosition = new Vector2(-RowInnerPadding, 0f);
    }
}
