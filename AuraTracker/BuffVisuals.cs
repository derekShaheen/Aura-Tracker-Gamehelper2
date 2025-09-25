using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using GameHelper.RemoteObjects;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameHelper.Utils;
using ImGuiNET;

namespace AuraTracker;

internal static class BuffVisuals
{
    internal struct BuffInfo
    {
        public string Name { get; set; }
        public int Stacks { get; set; }
        public float? DurationSeconds { get; set; }
        public string Display { get; set; }
        public float ChipWidth { get; set; }
        public float ChipHeight { get; set; }
    }

    public static List<BuffInfo> Extract(Entity entity, AuraTrackerSettings settings)
    {
        var map = new Dictionary<string, (int stacks, float? duration)>();

        try
        {
            if (!entity.TryGetComponent<Buffs>(out var component, true) || component == null)
            {
                return new List<BuffInfo>();
            }

            foreach (var kv in component.StatusEffects.ToArray())
            {
                string cleaned = CleanBuffName(kv.Key);
                if (string.IsNullOrEmpty(cleaned))
                {
                    continue;
                }

                int stacks = Math.Max(1, (int)kv.Value.Charges);
                float timeLeft = kv.Value.TimeLeft;
                float total = kv.Value.TotalTime;

                bool timeLeftFinite = !(float.IsNaN(timeLeft) || float.IsInfinity(timeLeft));
                bool totalFinite = !(float.IsNaN(total) || float.IsInfinity(total));
                float? duration = (settings.ShowDurations && timeLeftFinite && totalFinite && timeLeft > 0f) ? timeLeft : (float?)null;

                if (map.TryGetValue(cleaned, out var previous))
                {
                    int newStacks = previous.stacks + stacks;
                    float? newDuration = previous.duration.HasValue && duration.HasValue
                        ? MathF.Max(previous.duration.Value, duration.Value)
                        : previous.duration ?? duration;
                    map[cleaned] = (newStacks, newDuration);
                }
                else
                {
                    map[cleaned] = (stacks, duration);
                }
            }
        }
        catch
        {
        }

        var list = new List<BuffInfo>(map.Count);
        foreach (var kv in map)
        {
            list.Add(new BuffInfo
            {
                Name = kv.Key,
                Stacks = Math.Max(1, kv.Value.stacks),
                DurationSeconds = kv.Value.duration,
                Display = string.Empty,
                ChipWidth = 0f,
                ChipHeight = 0f
            });
        }

        return list;
    }

    public static void PopulateDisplayData(List<BuffInfo> buffs, AuraTrackerSettings settings)
    {
        if (buffs == null)
        {
            return;
        }

        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            buff.Display = ComposeDisplay(buff, settings);
            var size = ImGuiTextUtil.Measure(buff.Display, settings.BuffTextScale);
            buff.ChipWidth = size.X + 8f;
            buff.ChipHeight = size.Y + 4f;
            buffs[i] = buff;
        }
    }

    public static float MeasureHeight(List<BuffInfo> buffs, AuraTrackerSettings settings, float rowWidth)
    {
        float x = 0f;
        float y = 0f;
        float tallestRow = 0f;

        foreach (var buff in buffs)
        {
            var fitted = FitChip(buff, rowWidth, settings);
            float width = fitted.width;
            float height = fitted.height;

            if (x + width > rowWidth)
            {
                x = 0f;
                y += tallestRow + settings.BuffPad;
                tallestRow = 0f;
            }

            if (height > tallestRow)
            {
                tallestRow = height;
            }

            x += width + settings.BuffPad;
        }

        return y + MathF.Max(tallestRow, 0f);
    }

    public static float Draw(ImDrawListPtr drawList, Vector2 topLeft, List<BuffInfo> buffs, AuraTrackerSettings settings, float rowWidth)
    {
        float x = topLeft.X;
        float y = topLeft.Y;
        float rowRight = x + rowWidth;

        float tallestRow = 0f;
        float totalHeight = 0f;

        foreach (var buff in buffs)
        {
            var fitted = FitChip(buff, rowWidth, settings);
            string renderText = fitted.text;
            float width = fitted.width;
            float height = fitted.height;

            if (x + width > rowRight)
            {
                x = topLeft.X;
                y += tallestRow + settings.BuffPad;
                totalHeight += tallestRow + settings.BuffPad;
                tallestRow = 0f;
            }

            Vector4 baseColor;
            if (!TryGetOverrideColor(buff.Name, settings, out baseColor))
            {
                baseColor = HashToColor(buff.Name, settings.BuffBgAlpha, settings.ChipColorSeed);
            }

            uint fill = ImGuiHelper.Color(baseColor);
            uint border = ImGuiHelper.Color(new Vector4(baseColor.X * .55f, baseColor.Y * .55f, baseColor.Z * .55f, 0.9f));

            var rectMin = new Vector2(x, y);
            var rectMax = new Vector2(x + width, y + height);

            drawList.AddRectFilled(rectMin, rectMax, fill, settings.ChipCornerRadius);

            if (settings.FancyChipGloss && settings.ChipGlossAlpha > 0f)
            {
                float h = rectMax.Y - rectMin.Y;
                uint g1 = ImGuiHelper.Color(new Vector4(1, 1, 1, settings.ChipGlossAlpha));
                uint g2 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0f));
                drawList.AddRectFilledMultiColor(rectMin, new Vector2(rectMax.X, rectMin.Y + h * 0.55f), g1, g1, g2, g2);
            }

            drawList.AddRect(rectMin, rectMax, border, settings.ChipCornerRadius, 0, 1.0f);
            drawList.AddText(rectMin + new Vector2(4, 2), ImGuiHelper.Color(Vector4.One), renderText);

            if (height > tallestRow)
            {
                tallestRow = height;
            }

            x += width + settings.BuffPad;
        }

        totalHeight += tallestRow;
        return totalHeight;
    }

    public static List<BuffInfo> Arrange(List<BuffInfo> buffs, float rowWidth, AuraTrackerSettings settings)
    {
        if (buffs == null || buffs.Count == 0)
        {
            return buffs ?? new List<BuffInfo>();
        }

        var items = buffs.OrderByDescending(b => MathF.Min(b.ChipWidth, rowWidth)).ToList();
        var rows = new List<ChipRow>();

        foreach (var buff in items)
        {
            float chipWidth = MathF.Min(buff.ChipWidth, rowWidth);
            int bestIndex = -1;
            float bestLeftover = float.MaxValue;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float required = (row.Items.Count > 0 ? settings.BuffPad : 0f) + chipWidth;
                if (row.Used + required <= rowWidth)
                {
                    float leftover = rowWidth - (row.Used + required);
                    if (leftover < bestLeftover)
                    {
                        bestLeftover = leftover;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex == -1)
            {
                var newRow = new ChipRow();
                newRow.Items.Add(buff);
                newRow.Used = chipWidth;
                rows.Add(newRow);
            }
            else
            {
                var row = rows[bestIndex];
                row.Items.Add(buff);
                row.Used += (row.Items.Count > 1 ? settings.BuffPad : 0f) + chipWidth;
            }
        }

        return rows.SelectMany(r => r.Items).ToList();
    }

    public static void DrawHealthBar(ImDrawListPtr drawList, Vector2 topLeft, Life life, AuraTrackerSettings settings, float contentWidth)
    {
        float barWidth = contentWidth;
        var start = topLeft;
        var end = start + new Vector2(barWidth, settings.BarSize.Y);
        float radius = settings.BarCornerRadius;

        drawList.AddRectFilled(start, end, ImGuiHelper.Color(settings.BarBg), radius);

        int hpCurrent = Math.Max(life.Health.Current, 0);
        int hpTotal = Math.Max(life.Health.Total, 0);
        int esCurrent = Math.Max(life.EnergyShield.Current, 0);
        int esTotal = Math.Max(life.EnergyShield.Total, 0);

        int poolMax = Math.Max(1, hpTotal + esTotal);
        int poolCurrent = Math.Clamp(hpCurrent + esCurrent, 0, poolMax);

        float hpFraction = hpCurrent / (float)poolMax;
        float esFraction = esCurrent / (float)poolMax;

        float hpWidth = barWidth * Math.Clamp(hpFraction, 0f, 1f);
        float esWidth = barWidth * Math.Clamp(esFraction, 0f, 1f);

        if (hpWidth > 0.5f)
        {
            var hpEnd = new Vector2(start.X + hpWidth, end.Y);
            var hpFlag = ImDrawFlags.RoundCornersLeft | (esWidth <= 0.5f ? ImDrawFlags.RoundCornersRight : ImDrawFlags.None);
            drawList.AddRectFilled(start, hpEnd, ImGuiHelper.Color(settings.BarHpFill), radius, hpFlag);
        }

        if (esWidth > 0.5f)
        {
            var esStart = new Vector2(start.X + MathF.Max(hpWidth, 0f), start.Y);
            var esEnd = new Vector2(MathF.Min(esStart.X + esWidth, end.X), end.Y);
            var esFlag = (hpWidth <= 0.5f ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.None) | ImDrawFlags.RoundCornersRight;
            drawList.AddRectFilled(esStart, esEnd, ImGuiHelper.Color(settings.BarEsFill), radius, esFlag);

            if (settings.FancyEsDivider && hpWidth > 0.5f)
            {
                var x = start.X + hpWidth;
                var color = ImGuiHelper.Color(new Vector4(0, 0, 0, settings.EsDividerAlpha));
                drawList.AddLine(new Vector2(x, start.Y + 1), new Vector2(x, end.Y - 1), color, 1.0f);
            }
        }

        if (settings.FancyBarInnerBorder && settings.BarInnerBorderAlpha > 0f)
        {
            var borderColor = ImGuiHelper.Color(new Vector4(1, 1, 1, settings.BarInnerBorderAlpha * 0.15f));
            drawList.AddRect(start, end, borderColor, radius);
        }

        if (settings.FancyBarGloss)
        {
            float height = settings.BarSize.Y;
            uint c1 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.12f));
            uint c2 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.02f));
            drawList.AddRectFilledMultiColor(start, new Vector2(end.X, start.Y + height * 0.55f), c1, c1, c2, c2);
        }

        float pct = poolCurrent / (float)poolMax;
        string label = settings.ShowHpPercent ? $"{(int)(pct * 100f)}%" : NumberFormatter.Format(poolCurrent);
        var size = ImGui.CalcTextSize(label);
        var center = new Vector2(start.X + (barWidth - size.X) * 0.5f, start.Y + (settings.BarSize.Y - size.Y) * 0.5f);

        uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.90f));
        drawList.AddText(center + new Vector2(1, 0), shadow, label);
        drawList.AddText(center + new Vector2(-1, 0), shadow, label);
        drawList.AddText(center + new Vector2(0, 1), shadow, label);
        drawList.AddText(center + new Vector2(0, -1), shadow, label);
        drawList.AddText(center, ImGuiHelper.Color(Vector4.One), label);
    }

    private static (string text, float width, float height) FitChip(BuffInfo buff, float rowWidth, AuraTrackerSettings settings)
    {
        string stackSuffix = buff.Stacks > 1 ? $" x{buff.Stacks}" : string.Empty;
        string durationSuffix = (settings.ShowDurations && buff.DurationSeconds.HasValue) ? $" ({buff.DurationSeconds.Value:0}s)" : string.Empty;
        string suffix = stackSuffix + durationSuffix;

        string baseName = buff.Name;

        if (string.IsNullOrEmpty(suffix))
        {
            string renderOnly = ImGuiTextUtil.EllipsizeToWidth(baseName, rowWidth - 8f, settings.BuffTextScale);
            var sizeOnly = ImGuiTextUtil.Measure(renderOnly, settings.BuffTextScale);
            return (renderOnly, MathF.Min(rowWidth, sizeOnly.X + 8f), sizeOnly.Y + 4f);
        }

        var suffixSize = ImGuiTextUtil.Measure(suffix, settings.BuffTextScale);
        float suffixWidth = suffixSize.X;
        float availableForName = rowWidth - 8f - suffixWidth;

        if (availableForName <= 0f)
        {
            string suffixOnly = ImGuiTextUtil.EllipsizeToWidth(suffix, rowWidth - 8f, settings.BuffTextScale);
            var suffixOnlySize = ImGuiTextUtil.Measure(suffixOnly, settings.BuffTextScale);
            return (suffixOnly, MathF.Min(rowWidth, suffixOnlySize.X + 8f), suffixOnlySize.Y + 4f);
        }

        string nameFit = ImGuiTextUtil.EllipsizeToWidth(baseName, availableForName, settings.BuffTextScale);
        string render = nameFit + suffix;
        var allSize = ImGuiTextUtil.Measure(render, settings.BuffTextScale);
        return (render, MathF.Min(rowWidth, allSize.X + 8f), allSize.Y + 4f);
    }

    private static string ComposeDisplay(BuffInfo buff, AuraTrackerSettings settings)
    {
        string stack = buff.Stacks > 1 ? $" x{buff.Stacks}" : string.Empty;
        string duration = (settings.ShowDurations && buff.DurationSeconds.HasValue) ? $" ({buff.DurationSeconds.Value:0}s)" : string.Empty;
        return buff.Name + stack + duration;
    }

    private static string CleanBuffName(string raw)
    {
        string baseName = CleanBuffBase(raw);
        if (baseName == null)
        {
            return null;
        }

        return Titleize(baseName);
    }

    private static string CleanBuffBase(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string s = raw.Replace('_', ' ');

        string[] drop = { "visual", "visuals", "monster", "mod", "6B", "buff", "magic", "mob", "effect", "effects", "rare", "display", "not", "hidden", "epk", "rarity" };
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Where(w => !drop.Any(d => string.Equals(w, d, StringComparison.OrdinalIgnoreCase)));

        s = string.Join(' ', parts);
        s = Regex.Replace(s, "\\s+", " ").Trim();

        if (s.Length == 0)
        {
            return null;
        }

        return s;
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var words = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            words[i] = word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..];
        }

        return string.Join(' ', words);
    }

    private static bool TryGetOverrideColor(string baseChipName, AuraTrackerSettings settings, out Vector4 color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(baseChipName) || settings?.ChipOverrides == null)
        {
            return false;
        }

        foreach (var overrideEntry in settings.ChipOverrides)
        {
            if (!string.IsNullOrWhiteSpace(overrideEntry?.Match) && string.Equals(overrideEntry.Match.Trim(), baseChipName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                color = new Vector4(overrideEntry.Color.X, overrideEntry.Color.Y, overrideEntry.Color.Z, settings.BuffBgAlpha);
                return true;
            }
        }

        return false;
    }

    private static Vector4 HashToColor(string value, float alpha, int seed)
    {
        uint hash = 2166136261;
        unchecked
        {
            hash ^= (uint)seed;
            hash *= 16777619;
        }

        foreach (char c in value.ToUpperInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }

        float hue = (hash % 360) / 360f;
        HslToRgb(hue, 0.65f, 0.50f, out float r, out float g, out float b);
        return new Vector4(r, g, b, alpha);
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        float a = s * MathF.Min(l, 1 - l);
        float F(float n)
        {
            float k = (n + h * 12f) % 12f;
            return l - a * MathF.Max(-1, MathF.Min(MathF.Min(k - 3, 9 - k), 1));
        }

        r = F(0);
        g = F(8);
        b = F(4);
    }

    private sealed class ChipRow
    {
        public readonly List<BuffInfo> Items = new();
        public float Used;
    }
}
