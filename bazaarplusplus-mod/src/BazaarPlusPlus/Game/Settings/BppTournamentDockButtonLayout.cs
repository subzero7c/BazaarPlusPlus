#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal enum BppTournamentDockButtonSlot
{
    TournamentRoom = -1,
    CollectionPanel = 0,
    SettingsDock = 1,
}

internal static class BppTournamentDockButtonLayout
{
    internal const float RightMargin = 28f;
    internal const float ButtonGap = 18f;

    internal static Vector3 CalculateLocalPosition(
        RectTransform parentRect,
        RectTransform buttonRect,
        BppTournamentDockButtonSlot slot
    )
    {
        var canvasRect = ResolveCanvasRect(parentRect);
        var referenceRect = canvasRect ?? parentRect;
        var size = ResolveButtonSize(buttonRect);
        var x = referenceRect.rect.xMax - RightMargin - (size.x * 0.5f);
        var step = size.y + ButtonGap;
        var centerY = referenceRect.rect.center.y;
        var y = centerY - ((int)slot * step);
        var referenceLocalPosition = new Vector3(x, y, buttonRect.localPosition.z);
        if (canvasRect == null || canvasRect == parentRect)
            return referenceLocalPosition;

        var worldPosition = canvasRect.TransformPoint(referenceLocalPosition);
        var parentLocalPosition = parentRect.InverseTransformPoint(worldPosition);
        return new Vector3(parentLocalPosition.x, parentLocalPosition.y, buttonRect.localPosition.z);
    }

    private static RectTransform? ResolveCanvasRect(RectTransform parentRect)
    {
        var canvas = parentRect.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    private static Vector2 ResolveButtonSize(RectTransform buttonRect)
    {
        var size = buttonRect.rect.size;
        if (size.x <= 0.0001f || size.y <= 0.0001f)
            size = buttonRect.sizeDelta;

        if (size.x <= 0.0001f)
            size.x = 64f;
        if (size.y <= 0.0001f)
            size.y = 64f;

        return size;
    }
}
