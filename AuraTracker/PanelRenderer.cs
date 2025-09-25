using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GameHelper.RemoteEnums;
using GameHelper.Utils;
using ImGuiNET;

namespace AuraTracker;

internal sealed class PanelRenderer
{
    public void Render(List<MonsterCollector.MonsterSnapshot> monsters, AuraTrackerSettings settings, DpsTracker dpsTracker, Vector2 overlaySize)
    {
        if (monsters == null || monsters.Count == 0)
        {
            return;
        }

        float maxWidth = overlaySize.X - settings.LeftAnchor.X - settings.PanelRightSafeMargin;
        float contentWidth = MathF.Min(MathF.Max(settings.PanelWidth, 120f), MathF.Max(120f, maxWidth));

        var dpsMap = new Dictionary<uint, float>(monsters.Count);
        float totalDps = 0f;
        foreach (var row in monsters)
        {
            float dps = dpsTracker.Update(row.Entity.Id, row.Life, settings.DpsSmoothingSeconds);
            dpsMap[row.Entity.Id] = dps;
            totalDps += MathF.Max(0f, dps);
        }

        bool showHeader = settings.ShowOverallDps && monsters.Count >= 2;
        float headerHeight = 0f;
        string headerText = string.Empty;
        if (showHeader)
        {
            headerText = "TOTAL DPS " + NumberFormatter.Format((long)totalDps) + " ";
            headerHeight = ImGui.CalcTextSize(headerText).Y + 2f;
        }

        float usableMax = settings.MaxListHeight <= 0 ? overlaySize.Y : settings.MaxListHeight;
        float accumulatedHeight = showHeader ? headerHeight : 0f;

        var rows = new List<RowRenderData>();
        foreach (var snapshot in monsters)
        {
            var ordered = BuffVisuals.Arrange(snapshot.Buffs, contentWidth, settings);
            if (ordered.Count > settings.MaxBuffsPerEnemy)
            {
                ordered = ordered.Take(settings.MaxBuffsPerEnemy).ToList();
            }

            float nameHeight = ImGui.CalcTextSize(snapshot.Name).Y;
            float buffHeight = BuffVisuals.MeasureHeight(ordered, settings, contentWidth);
            float entryHeight = nameHeight + 2f + settings.BarSize.Y + settings.BarToBuffSpacing + buffHeight + settings.EntrySpacing;

            if (settings.LeftAnchor.Y + accumulatedHeight + entryHeight > usableMax)
            {
                break;
            }

            accumulatedHeight += entryHeight;
            rows.Add(new RowRenderData(snapshot, ordered, nameHeight));
        }

        if (rows.Count == 0)
        {
            return;
        }

        var drawList = ImGui.GetForegroundDrawList();

        if (settings.ShowPanelBackground)
        {
            var pMin = new Vector2(settings.LeftAnchor.X - settings.PanelPadding.X, settings.LeftAnchor.Y - settings.PanelPadding.Y);
            var pMax = new Vector2(settings.LeftAnchor.X - settings.PanelPadding.X + contentWidth + settings.PanelPadding.X * 2f,
                                   settings.LeftAnchor.Y - settings.PanelPadding.Y + accumulatedHeight + settings.PanelPadding.Y * 2f - settings.EntrySpacing);

            if (settings.FancyPanelShadow && settings.PanelShadowSize > 0f && settings.PanelShadowAlpha > 0f)
            {
                var shadowColor = ImGuiHelper.Color(new Vector4(0, 0, 0, settings.PanelShadowAlpha));
                for (int i = 0; i < 4; i++)
                {
                    float grow = settings.PanelShadowSize * (i + 1) / 4f;
                    drawList.AddRectFilled(pMin - new Vector2(grow, grow), pMax + new Vector2(grow, grow), shadowColor, settings.PanelCornerRadius + grow);
                }
            }

            drawList.AddRectFilled(pMin, pMax, ImGuiHelper.Color(settings.PanelBg), settings.PanelCornerRadius);

            if (settings.FancyRarityStripe && rows.Count > 0)
            {
                var rarest = rows.Select(r => r.Snapshot.Rarity).Max();
                var rarityColor = GetRarityColor(rarest);
                var stripeColor = ImGuiHelper.Color(new Vector4(rarityColor.X, rarityColor.Y, rarityColor.Z, 0.9f));
                var stripeMin = new Vector2(pMin.X, pMin.Y);
                var stripeMax = new Vector2(pMin.X + 3f, pMax.Y);
                drawList.AddRectFilled(stripeMin, stripeMax, stripeColor, settings.PanelCornerRadius);
            }

            drawList.AddRect(pMin, pMax, ImGuiHelper.Color(settings.PanelBorder), settings.PanelCornerRadius);
        }

        var cursor = settings.LeftAnchor;

        if (showHeader)
        {
            var headerSize = ImGui.CalcTextSize(headerText);
            var pos = new Vector2(settings.LeftAnchor.X + contentWidth - headerSize.X, cursor.Y);

            uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.80f));
            drawList.AddText(pos + new Vector2(1, 0), shadow, headerText);
            drawList.AddText(pos + new Vector2(0, 1), shadow, headerText);
            drawList.AddText(pos, ImGuiHelper.Color(settings.DpsTextColor), headerText);

            float separatorY = cursor.Y + headerSize.Y + 1f;
            uint separatorColor = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.08f));
            drawList.AddLine(new Vector2(settings.LeftAnchor.X, separatorY), new Vector2(settings.LeftAnchor.X + contentWidth, separatorY), separatorColor, 1f);

            cursor.Y += headerHeight;
        }

        float drawnHeight = 0f;
        foreach (var row in rows)
        {
            var snapshot = row.Snapshot;
            string nameToDraw = ImGuiTextUtil.EllipsizeToWidth(snapshot.Name, contentWidth, 1f);
            drawList.AddText(new Vector2(cursor.X, cursor.Y), ImGuiHelper.Color(GetRarityColor(snapshot.Rarity)), nameToDraw);

            float separatorY = cursor.Y + row.NameHeight + 1f;
            uint separatorColor = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.05f));
            drawList.AddLine(new Vector2(settings.LeftAnchor.X, separatorY), new Vector2(settings.LeftAnchor.X + contentWidth, separatorY), separatorColor, 1f);

            cursor.Y += row.NameHeight + 2f;

            var barTopLeft = new Vector2(cursor.X, cursor.Y);
            BuffVisuals.DrawHealthBar(drawList, barTopLeft, snapshot.Life, settings, contentWidth);

            if (settings.ShowDps)
            {
                float dpsValue = dpsMap.TryGetValue(snapshot.Entity.Id, out var d) ? d : 0f;
                string dpsText = "DPS " + NumberFormatter.Format((long)MathF.Max(0f, dpsValue));
                var size = ImGui.CalcTextSize(dpsText);
                var pos = new Vector2(barTopLeft.X + contentWidth - size.X - 4f, barTopLeft.Y + (settings.BarSize.Y - size.Y) * 0.5f);

                uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.80f));
                drawList.AddText(pos + new Vector2(1, 0), shadow, dpsText);
                drawList.AddText(pos + new Vector2(0, 1), shadow, dpsText);
                drawList.AddText(pos, ImGuiHelper.Color(settings.DpsTextColor), dpsText);
            }

            cursor.Y += settings.BarSize.Y + settings.BarToBuffSpacing;

            float usedBuffHeight = BuffVisuals.Draw(drawList, new Vector2(cursor.X, cursor.Y), row.Buffs, settings, contentWidth);

            cursor.Y += usedBuffHeight + settings.EntrySpacing;
            drawnHeight += row.NameHeight + 2f + settings.BarSize.Y + settings.BarToBuffSpacing + usedBuffHeight + settings.EntrySpacing;

            if (settings.LeftAnchor.Y + (showHeader ? headerHeight : 0f) + drawnHeight > usableMax)
            {
                break;
            }
        }
    }

    private static Vector4 GetRarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Normal => new Vector4(1f, 1f, 1f, 1f),
        Rarity.Magic => new Vector4(0.3f, 0.6f, 1f, 1f),
        Rarity.Rare => new Vector4(1f, 1f, 0f, 1f),
        Rarity.Unique => new Vector4(1f, 0.5f, 0f, 1f),
        _ => Vector4.One
    };

    private sealed class RowRenderData
    {
        public RowRenderData(MonsterCollector.MonsterSnapshot snapshot, List<BuffVisuals.BuffInfo> buffs, float nameHeight)
        {
            Snapshot = snapshot;
            Buffs = buffs;
            NameHeight = nameHeight;
        }

        public MonsterCollector.MonsterSnapshot Snapshot { get; }
        public List<BuffVisuals.BuffInfo> Buffs { get; }
        public float NameHeight { get; }
    }
}
