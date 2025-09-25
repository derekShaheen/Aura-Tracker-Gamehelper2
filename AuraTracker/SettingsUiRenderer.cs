using System;
using System.Numerics;
using GameHelper.RemoteEnums;
using ImGuiNET;

namespace AuraTracker;

internal sealed class SettingsUiRenderer
{
    private readonly string versionLabel;

    public SettingsUiRenderer(string version)
    {
        versionLabel = $"AuraTracker v{version} by Skrip";
    }

    public void Draw(AuraTrackerSettings settings)
    {
        if (ImGui.CollapsingHeader("General"))
        {
            if (ImGui.BeginTable("at_general", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox("Draw when game is backgrounded", ref settings.DrawWhenGameInBackground);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat("Screen Range (px)", ref settings.ScreenRangePx, 5f, 100f, 3000f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragInt("Max Enemies in List", ref settings.MaxEnemies, 1f, 1, 12);

                ImGui.TableNextColumn();
                string[] rarityNames = { "Normal", "Magic", "Rare", "Unique" };
                int curIdx = (int)settings.MinRarityToShow;
                if (ImGui.Combo("Min Rarity To Show", ref curIdx, rarityNames, rarityNames.Length))
                {
                    settings.MinRarityToShow = (Rarity)Math.Clamp(curIdx, 0, 3);
                }

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("List Layout"))
        {
            if (ImGui.BeginTable("at_layout", 2))
            {
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat2("Left Anchor (x,y)", ref settings.LeftAnchor, 1f, -4000, 4000);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat("Entry Spacing (px)", ref settings.EntrySpacing, 0.5f, 0f, 80f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat("Bar→Buff Spacing (px)", ref settings.BarToBuffSpacing, 0.5f, 0f, 40f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat("Panel Width (px)", ref settings.PanelWidth, 1f, 120f, 1600f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat("Max List Height (px, 0 = overlay)", ref settings.MaxListHeight, 5f, 0f, 8000f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat("Right Safe Margin (px)", ref settings.PanelRightSafeMargin, 0.5f, 0f, 120f);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Bar & Buffs"))
        {
            if (ImGui.BeginTable("at_bar", 2))
            {
                ImGui.TableNextColumn();
                ImGui.ColorEdit4("Bar Background", ref settings.BarBg);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4("HP Fill", ref settings.BarHpFill);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4("ES Fill", ref settings.BarEsFill);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat2("Bar Size (w,h)", ref settings.BarSize, 1f, 80, 600);

                ImGui.TableNextColumn();
                ImGui.Checkbox("HP Text Shows Percent (instead of absolute)", ref settings.ShowHpPercent);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat("Buff Padding (px)", ref settings.BuffPad, 0.5f, 0f, 16f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragInt("Max Buffs/Enemy", ref settings.MaxBuffsPerEnemy, 1f, 1, 30);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Show Buff Durations", ref settings.ShowDurations);

                ImGui.TableNextColumn();
                ImGui.SliderFloat("Buff BG Alpha", ref settings.BuffBgAlpha, 0.0f, 1.0f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat("Buff Text Scale", ref settings.BuffTextScale, 0.5f, 2.0f);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Show DPS Label", ref settings.ShowDps);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat("DPS Smoothing (s)", ref settings.DpsSmoothingSeconds, 0.05f, 0.1f, 5f);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4("DPS Text Color", ref settings.DpsTextColor);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Show Overall DPS Header", ref settings.ShowOverallDps);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Chip Color Overrides"))
        {
            ImGui.SetNextItemWidth(150);
            ImGui.DragInt("Chip Color Seed", ref settings.ChipColorSeed, 1, 0, 1000);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Set the seed used for randomizing buff chip background colors.\nSame seed yields same color mapping each launch.");
            }

            ImGui.TextWrapped("Add entries that match the chip's base text (without stacks or timer), e.g. \"Archnemesis\". The specified color overrides the random chip color. Alpha is ignored.");

            for (int i = 0; i < settings.ChipOverrides.Count; i++)
            {
                var item = settings.ChipOverrides[i];
                ImGui.PushID(i);

                if (ImGui.BeginTable("ovr_row", 3, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGui.TableSetupColumn("Del", ImGuiTableColumnFlags.WidthFixed, 60);

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var match = item.Match ?? string.Empty;
                    if (ImGui.InputText("##txt", ref match, 256))
                    {
                        item.Match = match;
                    }

                    ImGui.TableNextColumn();
                    var rgb = new Vector3(item.Color.X, item.Color.Y, item.Color.Z);
                    if (ImGui.ColorEdit3("##col", ref rgb, ImGuiColorEditFlags.NoInputs))
                    {
                        item.Color = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Remove"))
                    {
                        settings.ChipOverrides.RemoveAt(i);
                        ImGui.EndTable();
                        ImGui.PopID();
                        i--;
                        continue;
                    }

                    ImGui.EndTable();
                }

                settings.ChipOverrides[i] = item;
                ImGui.PopID();
            }

            if (ImGui.Button("Add Override"))
            {
                settings.ChipOverrides.Add(new AuraTrackerSettings.ChipColorOverride { Match = string.Empty, Color = new Vector4(1, 1, 1, 1) });
            }
        }

        if (ImGui.CollapsingHeader("Visuals"))
        {
            if (ImGui.BeginTable("at_fx", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox("Panel Shadow", ref settings.FancyPanelShadow);
                ImGui.TableNextColumn();
                ImGui.Checkbox("Rarity Stripe", ref settings.FancyRarityStripe);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(200);
                ImGui.DragFloat("Shadow Size", ref settings.PanelShadowSize, 0.5f, 0f, 40f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat("Shadow Alpha", ref settings.PanelShadowAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Bar Gloss", ref settings.FancyBarGloss);
                ImGui.TableNextColumn();
                ImGui.Checkbox("Bar Inner Border", ref settings.FancyBarInnerBorder);

                ImGui.TableNextColumn();
                ImGui.Checkbox("ES Divider", ref settings.FancyEsDivider);
                ImGui.TableNextColumn();
                ImGui.SliderFloat("ES Divider Alpha", ref settings.EsDividerAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat("Bar Corner Radius", ref settings.BarCornerRadius, 0f, 12f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat("Bar Inner Border Alpha", ref settings.BarInnerBorderAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Chip Gloss", ref settings.FancyChipGloss);

                ImGui.TableNextColumn();
                ImGui.SliderFloat("Chip Corner Radius", ref settings.ChipCornerRadius, 0f, 12f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat("Chip Gloss Alpha", ref settings.ChipGlossAlpha, 0f, 1f);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("List Background"))
        {
            if (ImGui.BeginTable("at_bg", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox("Show Panel Background", ref settings.ShowPanelBackground);
                ImGui.TableNextColumn();
                ImGui.ColorEdit4("Panel Background Color", ref settings.PanelBg);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4("Panel Border Color", ref settings.PanelBorder);
                ImGui.TableNextColumn();
                ImGui.DragFloat2("Panel Padding (x,y)", ref settings.PanelPadding, 0.5f, 0f, 40f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat("Panel Corner Radius", ref settings.PanelCornerRadius, 0f, 16f);
                ImGui.EndTable();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        float txtW = ImGui.CalcTextSize(versionLabel).X;
        float availW = ImGui.GetContentRegionAvail().X;
        float padX = MathF.Max(0f, (availW - txtW) * 0.5f);
        float curX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(curX + padX);
        ImGui.TextDisabled(versionLabel);
    }
}
