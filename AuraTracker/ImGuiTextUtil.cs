using System;
using System.Numerics;
using ImGuiNET;

namespace AuraTracker;

internal static class ImGuiTextUtil
{
    public static Vector2 Measure(string text, float scale)
    {
        ImGui.SetWindowFontScale(scale);
        var size = ImGui.CalcTextSize(text);
        ImGui.SetWindowFontScale(1f);
        return size;
    }

    public static float MeasureWidth(string text, float scale)
    {
        return Measure(text, scale).X;
    }

    public static string EllipsizeToWidth(string text, float maxWidth, float scale)
    {
        if (maxWidth <= 0)
        {
            return string.Empty;
        }

        if (MeasureWidth(text, scale) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        int low = 0;
        int high = text.Length;
        string best = string.Empty;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            string candidate = text.Substring(0, Math.Max(0, mid)) + ellipsis;
            float width = MeasureWidth(candidate, scale);
            if (width <= maxWidth)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }
}
