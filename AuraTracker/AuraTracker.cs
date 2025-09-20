using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;                // GameStateTypes, Rarity
using GameHelper.RemoteEnums.Entity;         // EntityTypes, EntityStates
using GameHelper.RemoteObjects;              // Entity
using GameHelper.RemoteObjects.Components;   // Render, Life, Buffs, ObjectMagicProperties
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameHelper.Utils;
using ImGuiNET;
using Newtonsoft.Json;

namespace AuraTracker
{
    public sealed class AuraTracker : PCore<AuraTrackerSettings>
    {
        private const string PluginVersion = "1.3.2";

        private readonly Dictionary<uint, Vector2> smoothPositions = new();
        private readonly Dictionary<uint, DpsState> dpsStates = new();
        private ActiveCoroutine onAreaChange;

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "AuraTracker.settings.json");

        public override void OnEnable(bool isGameOpened)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var txt = File.ReadAllText(SettingsPath);
                    this.Settings = JsonConvert.DeserializeObject<AuraTrackerSettings>(txt) ?? new AuraTrackerSettings();
                }
                else
                {
                    this.Settings = new AuraTrackerSettings();
                }
            }
            catch
            {
                this.Settings = new AuraTrackerSettings();
            }

            this.onAreaChange = CoroutineHandler.Start(OnAreaChange(), "", 0);
        }

        public override void OnDisable()
        {
            onAreaChange?.Cancel();
            onAreaChange = null;
            smoothPositions.Clear();
            dpsStates.Clear();
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.Text("AuraTracker — rarity-prioritized enemy list with buffs, fixed left panel.");
            ImGui.Separator();

            if (ImGui.CollapsingHeader("General"))
            {
                if (ImGui.BeginTable("at_general", 2))
                {

                    ImGui.TableNextColumn(); ImGui.Checkbox("Draw when game is backgrounded", ref Settings.DrawWhenGameInBackground);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat("Screen Range (px)", ref Settings.ScreenRangePx, 5f, 100f, 3000f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragInt("Max Enemies in List", ref Settings.MaxEnemies, 1f, 1, 12);

                    ImGui.TableNextColumn();
                    string[] rarityNames = { "Normal", "Magic", "Rare", "Unique" };
                    int curIdx = (int)Settings.MinRarityToShow;
                    if (ImGui.Combo("Min Rarity To Show", ref curIdx, rarityNames, rarityNames.Length))
                    {
                        Settings.MinRarityToShow = (Rarity)Math.Clamp(curIdx, 0, 3);
                    }
                    ImGui.EndTable();
                }
            }

            if (ImGui.CollapsingHeader("List Layout"))
            {
                if (ImGui.BeginTable("at_layout", 2))
                {
                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(220);
                    ImGui.DragFloat2("Left Anchor (x,y)", ref Settings.LeftAnchor, 1f, -4000, 4000);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat("Entry Spacing (px)", ref Settings.EntrySpacing, 0.5f, 0f, 80f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat("Bar→Buff Spacing (px)", ref Settings.BarToBuffSpacing, 0.5f, 0f, 40f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(220);
                    ImGui.DragFloat("Panel Width (px)", ref Settings.PanelWidth, 1f, 120f, 1600f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(220);
                    ImGui.DragFloat("Max List Height (px, 0 = overlay)", ref Settings.MaxListHeight, 5f, 0f, 8000f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(220);
                    ImGui.DragFloat("Right Safe Margin (px)", ref Settings.PanelRightSafeMargin, 0.5f, 0f, 120f);

                    ImGui.EndTable();
                }
            }

            if (ImGui.CollapsingHeader("Bar & Buffs"))
            {
                if (ImGui.BeginTable("at_bar", 2))
                {
                    ImGui.TableNextColumn(); ImGui.ColorEdit4("Bar Background", ref Settings.BarBg);
                    ImGui.TableNextColumn(); ImGui.ColorEdit4("HP Fill", ref Settings.BarHpFill);

                    ImGui.TableNextColumn(); ImGui.ColorEdit4("ES Fill", ref Settings.BarEsFill);
                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat2("Bar Size (w,h)", ref Settings.BarSize, 1f, 80, 600);

                    ImGui.TableNextColumn(); ImGui.Checkbox("HP Text Shows Percent (instead of absolute)", ref Settings.ShowHpPercent);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat("Buff Padding (px)", ref Settings.BuffPad, 0.5f, 0f, 16f);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragInt("Max Buffs/Enemy", ref Settings.MaxBuffsPerEnemy, 1f, 1, 30);

                    ImGui.TableNextColumn(); ImGui.Checkbox("Show Buff Durations (finite only)", ref Settings.ShowDurations);

                    ImGui.TableNextColumn(); ImGui.SliderFloat("Buff BG Alpha", ref Settings.BuffBgAlpha, 0.0f, 1.0f);
                    ImGui.TableNextColumn(); ImGui.SliderFloat("Buff Text Scale", ref Settings.BuffTextScale, 0.5f, 2.0f);

                    // --- DPS ---
                    ImGui.TableNextColumn(); ImGui.Checkbox("Show DPS Label", ref Settings.ShowDps);
                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(180);
                    ImGui.DragFloat("DPS Smoothing (s)", ref Settings.DpsSmoothingSeconds, 0.05f, 0.1f, 5f);

                    ImGui.TableNextColumn(); ImGui.ColorEdit4("DPS Text Color", ref Settings.DpsTextColor);

                    // NEW: Overall DPS header toggle
                    ImGui.TableNextColumn(); ImGui.Checkbox("Show Overall DPS Header", ref Settings.ShowOverallDps);

                    ImGui.EndTable();
                }
            }

            if (ImGui.CollapsingHeader("Fancy Visuals"))
            {
                if (ImGui.BeginTable("at_fx", 2))
                {
                    ImGui.TableNextColumn(); ImGui.Checkbox("Panel Shadow", ref Settings.FancyPanelShadow);
                    ImGui.TableNextColumn(); ImGui.Checkbox("Rarity Stripe", ref Settings.FancyRarityStripe);

                    ImGui.TableNextColumn(); ImGui.SetNextItemWidth(200);
                    ImGui.DragFloat("Shadow Size", ref Settings.PanelShadowSize, 0.5f, 0f, 40f);
                    ImGui.TableNextColumn(); ImGui.SliderFloat("Shadow Alpha", ref Settings.PanelShadowAlpha, 0f, 1f);

                    ImGui.TableNextColumn(); ImGui.Checkbox("Bar Gloss", ref Settings.FancyBarGloss);
                    ImGui.TableNextColumn(); ImGui.Checkbox("Bar Inner Border", ref Settings.FancyBarInnerBorder);

                    ImGui.TableNextColumn(); ImGui.Checkbox("ES Divider", ref Settings.FancyEsDivider);
                    ImGui.TableNextColumn(); ImGui.SliderFloat("ES Divider Alpha", ref Settings.EsDividerAlpha, 0f, 1f);

                    ImGui.TableNextColumn(); ImGui.SliderFloat("Bar Corner Radius", ref Settings.BarCornerRadius, 0f, 12f);
                    ImGui.TableNextColumn(); ImGui.SliderFloat("Bar Inner Border Alpha", ref Settings.BarInnerBorderAlpha, 0f, 1f);

                    ImGui.TableNextColumn(); ImGui.Checkbox("Chip Gloss", ref Settings.FancyChipGloss);

                    ImGui.TableNextColumn(); ImGui.SliderFloat("Chip Corner Radius", ref Settings.ChipCornerRadius, 0f, 12f);
                    ImGui.TableNextColumn(); ImGui.SliderFloat("Chip Gloss Alpha", ref Settings.ChipGlossAlpha, 0f, 1f);

                    ImGui.EndTable();
                }
            }

            if (ImGui.CollapsingHeader("List Background"))
            {
                if (ImGui.BeginTable("at_bg", 2))
                {
                    ImGui.TableNextColumn(); ImGui.Checkbox("Show Panel Background", ref Settings.ShowPanelBackground);
                    ImGui.TableNextColumn(); ImGui.ColorEdit4("Panel Background Color", ref Settings.PanelBg);

                    ImGui.TableNextColumn(); ImGui.ColorEdit4("Panel Border Color", ref Settings.PanelBorder);
                    ImGui.TableNextColumn(); ImGui.DragFloat2("Panel Padding (x,y)", ref Settings.PanelPadding, 0.5f, 0f, 40f);

                    ImGui.TableNextColumn(); ImGui.SliderFloat("Panel Corner Radius", ref Settings.PanelCornerRadius, 0f, 16f);
                    ImGui.EndTable();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Center the version label in the settings panel
            string verLabel = $"AuraTracker v{PluginVersion} by Skrip";
            float txtW = ImGui.CalcTextSize(verLabel).X;
            float availW = ImGui.GetContentRegionAvail().X;
            float padX = MathF.Max(0f, (availW - txtW) * 0.5f);
            float curX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(curX + padX);

            // Slightly muted look
            ImGui.TextDisabled(verLabel);
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState && Core.States.GameCurrentState != GameStateTypes.EscapeState) return;

            var inGame = Core.States.InGameStateObject;
            var world = inGame.CurrentWorldInstance;
            var area = inGame.CurrentAreaInstance;

            if (!Settings.DrawWhenGameInBackground && !Core.Process.Foreground) return;
            if (inGame.GameUi.SkillTreeNodesUiElements.Count > 0) return;

            var candidates = new List<(Entity e, Vector2 screen, Life life, Rarity rarity, List<BuffInfo> buffs, string name, float nameW, float maxChipW)>();

            foreach (var kv in area.AwakeEntities)
            {
                var e = kv.Value;

                // Validity/state filter (and exclude friendlies)
                if (!e.IsValid || e.EntityState == EntityStates.PinnacleBossHidden || e.EntityState == EntityStates.Useless || e.EntityState == EntityStates.MonsterFriendly)
                    continue;

                if (e.EntityType != EntityTypes.Monster) continue;

                if (e.EntitySubtype == EntitySubtypes.PlayerOther || e.EntitySubtype == EntitySubtypes.PlayerSelf) continue;

                // rarity
                if (!TryGetRarity(e, out Rarity rarity)) continue;
                if (rarity < Settings.MinRarityToShow) continue;

                // Render → screen for proximity
                if (!e.TryGetComponent<Render>(out var r, true)) continue;

                var pos = r.WorldPosition;
                pos.Z -= r.ModelBounds.Z;
                var screen = world.WorldToScreen(pos, pos.Z);

                var center = new Vector2(Core.Overlay.Size.Width / 2f, Core.Overlay.Size.Height / 2f);
                if (Vector2.Distance(screen, center) > Settings.ScreenRangePx)
                    continue;

                // life
                if (!e.TryGetComponent<Life>(out var life, true)) continue;

                // buffs
                var buffs = ExtractBuffs(e, Settings);

                // Precompute chip sizes
                float rowMaxChipW = 0f;
                for (int i = 0; i < buffs.Count; i++)
                {
                    var b = buffs[i];
                    string disp = ComposeBuffDisplay(b, Settings);
                    var sz = MeasureText(disp, Settings.BuffTextScale);
                    b.Display = disp;
                    b.ChipWidth = sz.X + 8f;
                    b.ChipHeight = sz.Y + 4f;
                    buffs[i] = b;
                    if (b.ChipWidth > rowMaxChipW) rowMaxChipW = b.ChipWidth;
                }

                // name
                string name = GetMonsterName(e) ?? "Unknown";
                float nameW = MeasureTextWidth(name, 1.0f);

                candidates.Add((e, screen, life, rarity, buffs, name, nameW, rowMaxChipW));
            }

            // Hard-dedupe by entity id.
            candidates = candidates
                .GroupBy(t => t.e.Id)
                .Select(g => g.First())
                .ToList();

            // Rarity-prioritized selection
            var centerPt = new Vector2(Core.Overlay.Size.Width / 2f, Core.Overlay.Size.Height / 2f);
            var selected = new List<(Entity e, Vector2 screen, Life life, Rarity rarity, List<BuffInfo> buffs, string name, float nameW, float maxChipW)>();
            var usedIds = new HashSet<uint>();
            int slots = Math.Max(0, Settings.MaxEnemies);
            Rarity[] order = { Rarity.Unique, Rarity.Rare, Rarity.Magic, Rarity.Normal };

            foreach (var rr in order)
            {
                if (slots <= 0) break;

                foreach (var item in candidates.Where(t => t.rarity == rr)
                                               .OrderBy(t => Vector2.Distance(t.screen, centerPt)))
                {
                    if (slots <= 0) break;
                    if (!usedIds.Add(item.e.Id)) continue;
                    selected.Add(item);
                    slots--;
                }
            }

            candidates = selected;
            if (candidates.Count == 0) return;

            // Precompute DPS (once) so we can draw a header and reuse per-row without double-sampling.
            var dpsMap = new Dictionary<uint, float>(candidates.Count);
            float totalDps = 0f;
            foreach (var row in candidates)
            {
                float d = UpdateAndGetDps(row.e.Id, row.life);
                dpsMap[row.e.Id] = d;
                totalDps += MathF.Max(0f, d);
            }

            // Panel width
            float maxW = Core.Overlay.Size.Width - Settings.LeftAnchor.X - Settings.PanelRightSafeMargin;
            float contentW = MathF.Min(MathF.Max(Settings.PanelWidth, 120f), MathF.Max(120f, maxW));

            // Optional header?
            bool showHeader = Settings.ShowOverallDps && candidates.Count >= 2;
            float headerH = 0f;
            string headerText = "";
            if (showHeader)
            {
                headerText = "TOTAL DPS " + HumanizeNumber((long)totalDps) + " ";
                headerH = ImGui.CalcTextSize(headerText).Y + 2f; // + small spacing
            }

            // Total height prepass
            float totalHeight = 0f;
            float usableMax = Settings.MaxListHeight <= 0 ? Core.Overlay.Size.Height : Settings.MaxListHeight;

            // include header height if shown
            totalHeight += showHeader ? headerH : 0f;

            foreach (var row in candidates)
            {
                var ordered = CartonizeBuffs(row.buffs, contentW, Settings);
                if (ordered.Count > Settings.MaxBuffsPerEnemy)
                    ordered = ordered.Take(Settings.MaxBuffsPerEnemy).ToList();

                float nameH = ImGui.CalcTextSize(row.name).Y;
                float buffsH = MeasureBuffsHeight(ordered, Settings, contentW);
                float entryH = nameH + 2f + Settings.BarSize.Y + Settings.BarToBuffSpacing + buffsH + Settings.EntrySpacing;

                if (Settings.LeftAnchor.Y + totalHeight + entryH > usableMax) break;
                totalHeight += entryH;
            }
            if (totalHeight <= 0f) return;

            var dl = ImGui.GetBackgroundDrawList();

            // Panel background (with fancy options)
            if (Settings.ShowPanelBackground)
            {
                var pMin = new Vector2(Settings.LeftAnchor.X - Settings.PanelPadding.X,
                                       Settings.LeftAnchor.Y - Settings.PanelPadding.Y);
                var pMax = new Vector2(Settings.LeftAnchor.X - Settings.PanelPadding.X + contentW + Settings.PanelPadding.X * 2f,
                                       Settings.LeftAnchor.Y - Settings.PanelPadding.Y + totalHeight + Settings.PanelPadding.Y * 2f - Settings.EntrySpacing);

                if (Settings.FancyPanelShadow && Settings.PanelShadowSize > 0f && Settings.PanelShadowAlpha > 0f)
                {
                    var shadowCol = ImGuiHelper.Color(new Vector4(0, 0, 0, Settings.PanelShadowAlpha));
                    for (int i = 0; i < 4; i++)
                    {
                        float grow = Settings.PanelShadowSize * (i + 1) / 4f;
                        dl.AddRectFilled(pMin - new Vector2(grow, grow), pMax + new Vector2(grow, grow), shadowCol, Settings.PanelCornerRadius + grow);
                    }
                }

                dl.AddRectFilled(pMin, pMax, ImGuiHelper.Color(Settings.PanelBg), Settings.PanelCornerRadius);

                if (Settings.FancyRarityStripe && candidates.Count > 0)
                {
                    var rarest = candidates.Select(c => c.rarity).Max();
                    var rc = RarityColor(rarest);
                    var stripeCol = ImGuiHelper.Color(new Vector4(rc.X, rc.Y, rc.Z, 0.9f));
                    var stripeMin = new Vector2(pMin.X, pMin.Y);
                    var stripeMax = new Vector2(pMin.X + 3f, pMax.Y);
                    dl.AddRectFilled(stripeMin, stripeMax, stripeCol, Settings.PanelCornerRadius);
                }

                dl.AddRect(pMin, pMax, ImGuiHelper.Color(Settings.PanelBorder), Settings.PanelCornerRadius);
            }

            // Draw header (if enabled and 2+ rows)
            var cursor = Settings.LeftAnchor;
            if (showHeader)
            {
                // Right-align the TOTAL DPS header within the content area
                var hdrSz = ImGui.CalcTextSize(headerText);
                var pos = new Vector2(Settings.LeftAnchor.X + contentW - hdrSz.X, cursor.Y);

                uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.80f));
                dl.AddText(pos + new Vector2(1, 0), shadow, headerText);
                dl.AddText(pos + new Vector2(0, 1), shadow, headerText);
                dl.AddText(pos, ImGuiHelper.Color(Settings.DpsTextColor), headerText);

                // Divider under header (still spans full width)
                float sepY = cursor.Y + hdrSz.Y + 1f;
                uint sepCol = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.08f));
                dl.AddLine(new Vector2(Settings.LeftAnchor.X, sepY), new Vector2(Settings.LeftAnchor.X + contentW, sepY), sepCol, 1f);

                cursor.Y += headerH;
            }


            // Draw entries — left-aligned
            float drawn = 0f;
            foreach (var row in candidates)
            {
                float nameH = ImGui.CalcTextSize(row.name).Y;

                // Name
                string nameToDraw = EllipsizeToWidth(row.name, contentW, 1f);
                dl.AddText(new Vector2(cursor.X, cursor.Y), ImGuiHelper.Color(RarityColor(row.rarity)), nameToDraw);

                // Separator
                float sepY = cursor.Y + nameH + 1f;
                uint sepCol = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.05f));
                dl.AddLine(new Vector2(Settings.LeftAnchor.X, sepY), new Vector2(Settings.LeftAnchor.X + contentW, sepY), sepCol, 1f);

                cursor.Y += nameH + 2f;

                // Health bar
                var barTopLeft = new Vector2(cursor.X, cursor.Y);
                DrawBarLeft(dl, barTopLeft, row.life, Settings, contentW);

                // DPS label (right-aligned, vertically centered inside the bar)
                if (Settings.ShowDps)
                {
                    float dps = dpsMap.TryGetValue(row.e.Id, out var d) ? d : 0f;
                    string dpsText = "DPS " + HumanizeNumber((long)MathF.Max(0f, dps));
                    var sz = ImGui.CalcTextSize(dpsText);

                    var pos = new Vector2(
                        barTopLeft.X + contentW - sz.X - 4f,
                        barTopLeft.Y + (Settings.BarSize.Y - sz.Y) * 0.5f
                    );

                    uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.80f));
                    dl.AddText(pos + new Vector2(1, 0), shadow, dpsText);
                    dl.AddText(pos + new Vector2(0, 1), shadow, dpsText);
                    dl.AddText(pos, ImGuiHelper.Color(Settings.DpsTextColor), dpsText);
                }

                cursor.Y += Settings.BarSize.Y + Settings.BarToBuffSpacing;

                // Buffs
                var ordered = CartonizeBuffs(row.buffs, contentW, Settings);
                if (ordered.Count > Settings.MaxBuffsPerEnemy)
                    ordered = ordered.Take(Settings.MaxBuffsPerEnemy).ToList();

                float usedBuff = DrawBuffsLeft(dl, new Vector2(cursor.X, cursor.Y), ordered, Settings, contentW);

                cursor.Y += usedBuff + Settings.EntrySpacing;
                drawn += nameH + 2f + Settings.BarSize.Y + Settings.BarToBuffSpacing + usedBuff + Settings.EntrySpacing;
                if (Settings.LeftAnchor.Y + (showHeader ? headerH : 0f) + drawn > usableMax) break;
            }
        }

        // ---------- DPS tracking ----------

        private sealed class DpsState
        {
            public int LastPool;     // last (HP+ES)
            public long LastTicks;   // DateTime.UtcNow.Ticks
            public float Ema;        // smoothed DPS
        }

        private float UpdateAndGetDps(uint id, Life life)
        {
            int hpCur = Math.Max(life.Health.Current, 0);
            int esCur = Math.Max(life.EnergyShield.Current, 0);
            int pool = hpCur + esCur;

            long nowTicks = DateTime.UtcNow.Ticks;
            float dt = 0f;

            if (!dpsStates.TryGetValue(id, out var st))
            {
                st = new DpsState { LastPool = pool, LastTicks = nowTicks, Ema = 0f };
                dpsStates[id] = st;
                return 0f;
            }

            dt = MathF.Max(0f, (nowTicks - st.LastTicks) / 10_000_000f); // ticks->seconds
            st.LastTicks = nowTicks;

            if (dt > 0f)
            {
                int delta = st.LastPool - pool; // positive when taking damage
                st.LastPool = pool;

                float sample = delta > 0 ? delta / dt : 0f;

                // Exponential smoothing with time-constant = Settings.DpsSmoothingSeconds
                float tau = MathF.Max(0.1f, Settings.DpsSmoothingSeconds);
                float alpha = 1f - MathF.Exp(-dt / tau);

                // If no damage this frame, decay toward 0 smoothly
                float target = sample > 0 ? sample : 0f;
                st.Ema = st.Ema + alpha * (target - st.Ema);
            }

            dpsStates[id] = st;
            return st.Ema;
        }

        // ---------- helpers (unchanged from your polished build) ----------

        private static bool TryGetRarity(Entity e, out Rarity rarity)
        {
            rarity = Rarity.Normal;
            try
            {
                if (e.TryGetComponent<ObjectMagicProperties>(out var omp, true))
                {
                    rarity = omp.Rarity;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static Vector4 RarityColor(Rarity r) => r switch
        {
            Rarity.Normal => new(1f, 1f, 1f, 1f),
            Rarity.Magic => new(0.3f, 0.6f, 1f, 1f),
            Rarity.Rare => new(1f, 1f, 0f, 1f),
            Rarity.Unique => new(1f, 0.5f, 0f, 1f),
            _ => Vector4.One
        };

        private static string GetMonsterName(Entity e)
        {
            try
            {
                string p = e.Path;
                if (string.IsNullOrEmpty(p)) return null;

                int slash = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
                string tail = (slash >= 0) ? p[(slash + 1)..] : p;

                int at = tail.IndexOf('@');
                if (at >= 0) tail = tail[..at];

                tail = tail.Replace('_', ' ').Trim();
                if (tail.Length == 0) return null;

                var sb = new StringBuilder(tail.Length * 2);
                sb.Append(tail[0]);
                for (int i = 1; i < tail.Length; i++)
                {
                    char c = tail[i];
                    if (char.IsUpper(c)) sb.Append(' ');
                    sb.Append(c);
                }
                string spaced = sb.ToString().Trim();

                if (spaced.Length > 0 && char.IsLetter(spaced[0]))
                    spaced = char.ToUpperInvariant(spaced[0]) + (spaced.Length > 1 ? spaced[1..] : "");

                return spaced;
            }
            catch { return null; }
        }

        private static string CleanBuffBase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Replace('_', ' ');

            string[] drop = { "visual", "visuals", "monster", "mod", "6B", "buff", "magic", "mob", "effect", "effects", "rare", "display", "not", "hidden", "epk", "rarity" };
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => !drop.Any(d => string.Equals(w, d, StringComparison.OrdinalIgnoreCase)));

            s = string.Join(' ', parts);

            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length == 0) return null;

            return s;
        }

        private static string Titleize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            var words = s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                words[i] = w.Length == 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..];
            }
            return string.Join(' ', words);
        }

        private static string CleanBuffName(string raw)
        {
            string baseName = CleanBuffBase(raw);
            if (baseName == null) return null;

            if (string.Equals(baseName, "hidden", StringComparison.OrdinalIgnoreCase))
                return null;

            return Titleize(baseName);
        }

        private static string ComposeBuffDisplay(BuffInfo b, AuraTrackerSettings s)
        {
            string stack = b.Stacks > 1 ? $" x{b.Stacks}" : "";
            string dur = (s.ShowDurations && b.DurationSeconds.HasValue) ? $" ({b.DurationSeconds.Value:0}s)" : "";
            return b.Name + stack + dur;
        }

        private static (string text, float width, float height) FitChipToWidth(BuffInfo b, float rowWidth, AuraTrackerSettings s)
        {
            string stackSuffix = b.Stacks > 1 ? $" x{b.Stacks}" : "";
            string durSuffix = (s.ShowDurations && b.DurationSeconds.HasValue) ? $" ({b.DurationSeconds.Value:0}s)" : "";
            string suffix = stackSuffix + durSuffix;

            string baseName = b.Name;

            if (string.IsNullOrEmpty(suffix))
            {
                string renderOnly = EllipsizeToWidth(baseName, rowWidth - 8f, s.BuffTextScale);
                var sizeOnly = MeasureText(renderOnly, s.BuffTextScale);
                return (renderOnly, MathF.Min(rowWidth, sizeOnly.X + 8f), sizeOnly.Y + 4f);
            }

            var sufSz = MeasureText(suffix, s.BuffTextScale);
            float sufW = sufSz.X;
            float availForName = rowWidth - 8f - sufW;

            if (availForName <= 0f)
            {
                string sufOnly = EllipsizeToWidth(suffix, rowWidth - 8f, s.BuffTextScale);
                var sufOnlySz = MeasureText(sufOnly, s.BuffTextScale);
                return (sufOnly, MathF.Min(rowWidth, sufOnlySz.X + 8f), sufOnlySz.Y + 4f);
            }

            string nameFit = EllipsizeToWidth(baseName, availForName, s.BuffTextScale);
            string render = nameFit + suffix;
            var allSz = MeasureText(render, s.BuffTextScale);
            return (render, MathF.Min(rowWidth, allSz.X + 8f), allSz.Y + 4f);
        }

        private static float MeasureBuffsHeight(List<BuffInfo> buffs, AuraTrackerSettings s, float rowWidth)
        {
            float x = 0f, y = 0f, tallestRow = 0f;

            foreach (var b in buffs)
            {
                var fitted = FitChipToWidth(b, rowWidth, s);
                float width = fitted.width;
                float height = fitted.height;

                if (x + width > rowWidth)
                {
                    x = 0f;
                    y += tallestRow + s.BuffPad;
                    tallestRow = 0f;
                }

                if (height > tallestRow) tallestRow = height;
                x += width + s.BuffPad;
            }

            return y + MathF.Max(tallestRow, 0f);
        }

        private static float DrawBuffsLeft(ImDrawListPtr dl, Vector2 topLeft, List<BuffInfo> buffs, AuraTrackerSettings s, float rowWidth)
        {
            float x = topLeft.X;
            float y = topLeft.Y;
            float rowRight = x + rowWidth;

            float tallestRow = 0f;
            float totalHeight = 0f;

            foreach (var b in buffs)
            {
                var fitted = FitChipToWidth(b, rowWidth, s);
                string renderText = fitted.text;
                float width = fitted.width;
                float height = fitted.height;

                if (x + width > rowRight)
                {
                    x = topLeft.X;
                    y += tallestRow + s.BuffPad;
                    totalHeight += tallestRow + s.BuffPad;
                    tallestRow = 0f;
                }

                Vector4 baseCol = HashToColor(b.Name, s.BuffBgAlpha);
                uint fill = ImGuiHelper.Color(baseCol);
                uint border = ImGuiHelper.Color(new Vector4(baseCol.X * .55f, baseCol.Y * .55f, baseCol.Z * .55f, 0.9f));

                var rectMin = new Vector2(x, y);
                var rectMax = new Vector2(x + width, y + height);

                // Fill
                dl.AddRectFilled(rectMin, rectMax, fill, s.ChipCornerRadius);

                // Gloss
                if (s.FancyChipGloss && s.ChipGlossAlpha > 0f)
                {
                    float h = rectMax.Y - rectMin.Y;
                    uint g1 = ImGuiHelper.Color(new Vector4(1, 1, 1, s.ChipGlossAlpha));
                    uint g2 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0f));
                    dl.AddRectFilledMultiColor(
                        rectMin,
                        new Vector2(rectMax.X, rectMin.Y + h * 0.55f),
                        g1, g1, g2, g2
                    );
                }

                // Border
                dl.AddRect(rectMin, rectMax, border, s.ChipCornerRadius, 0, 1.0f);

                // Text
                dl.AddText(rectMin + new Vector2(4, 2), ImGuiHelper.Color(Vector4.One), renderText);

                if (height > tallestRow) tallestRow = height;
                x += width + s.BuffPad;
            }

            totalHeight += tallestRow;
            return totalHeight;
        }

        private sealed class ChipRow { public readonly List<BuffInfo> Items = new(); public float Used; }

        private static List<BuffInfo> CartonizeBuffs(List<BuffInfo> buffs, float rowWidth, AuraTrackerSettings s)
        {
            if (buffs == null || buffs.Count == 0) return buffs ?? new List<BuffInfo>();
            var items = buffs.OrderByDescending(b => MathF.Min(b.ChipWidth, rowWidth)).ToList();
            var rows = new List<ChipRow>();

            foreach (var b in items)
            {
                float chipW = MathF.Min(b.ChipWidth, rowWidth);
                int bestIdx = -1;
                float bestLeftover = float.MaxValue;

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    float required = (row.Items.Count > 0 ? s.BuffPad : 0f) + chipW;
                    if (row.Used + required <= rowWidth)
                    {
                        float leftover = rowWidth - (row.Used + required);
                        if (leftover < bestLeftover)
                        {
                            bestLeftover = leftover;
                            bestIdx = i;
                        }
                    }
                }

                if (bestIdx == -1)
                {
                    var r = new ChipRow();
                    r.Items.Add(b);
                    r.Used = chipW;
                    rows.Add(r);
                }
                else
                {
                    var r = rows[bestIdx];
                    r.Items.Add(b);
                    r.Used += (r.Items.Count > 1 ? s.BuffPad : 0f) + chipW;
                }
            }

            return rows.SelectMany(r => r.Items).ToList();
        }

        private static void DrawBarLeft(ImDrawListPtr dl, Vector2 topLeft, Life life, AuraTrackerSettings s, float contentW)
        {
            float barW = contentW;
            var start = topLeft;
            var end = start + new Vector2(barW, s.BarSize.Y);
            float r = s.BarCornerRadius;

            // Background
            dl.AddRectFilled(start, end, ImGuiHelper.Color(s.BarBg), r);

            int hpCur = Math.Max(life.Health.Current, 0);
            int hpMax = Math.Max(life.Health.Total, 0);
            int esCur = Math.Max(life.EnergyShield.Current, 0);
            int esMax = Math.Max(life.EnergyShield.Total, 0);

            int poolMax = Math.Max(1, hpMax + esMax);
            int poolCur = Math.Clamp(hpCur + esCur, 0, poolMax);

            float hpFracCur = hpCur / (float)poolMax;
            float esFracCur = esCur / (float)poolMax;

            float hpW = barW * Math.Clamp(hpFracCur, 0f, 1f);
            float esW = barW * Math.Clamp(esFracCur, 0f, 1f);

            // HP segment
            if (hpW > 0.5f)
            {
                var hpEnd = new Vector2(start.X + hpW, end.Y);
                var hpFlag = ImDrawFlags.RoundCornersLeft | (esW <= 0.5f ? ImDrawFlags.RoundCornersRight : ImDrawFlags.None);
                dl.AddRectFilled(start, hpEnd, ImGuiHelper.Color(s.BarHpFill), r, hpFlag);
            }

            // ES segment
            if (esW > 0.5f)
            {
                var esStart = new Vector2(start.X + MathF.Max(hpW, 0f), start.Y);
                var esEnd = new Vector2(MathF.Min(esStart.X + esW, end.X), end.Y);
                var esFlag = (hpW <= 0.5f ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.None) | ImDrawFlags.RoundCornersRight;
                dl.AddRectFilled(esStart, esEnd, ImGuiHelper.Color(s.BarEsFill), r, esFlag);

                if (s.FancyEsDivider && hpW > 0.5f)
                {
                    var x = start.X + hpW;
                    var col = ImGuiHelper.Color(new Vector4(0, 0, 0, s.EsDividerAlpha));
                    dl.AddLine(new Vector2(x, start.Y + 1), new Vector2(x, end.Y - 1), col, 1.0f);
                }
            }

            // Inner border
            if (s.FancyBarInnerBorder && s.BarInnerBorderAlpha > 0f)
            {
                var borderCol = ImGuiHelper.Color(new Vector4(1, 1, 1, s.BarInnerBorderAlpha * 0.15f));
                dl.AddRect(start, end, borderCol, r);
            }

            // Gloss pass
            if (s.FancyBarGloss)
            {
                float h = s.BarSize.Y;
                uint c1 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.12f));
                uint c2 = ImGuiHelper.Color(new Vector4(1, 1, 1, 0.02f));
                dl.AddRectFilledMultiColor(
                    start,
                    new Vector2(end.X, start.Y + h * 0.55f),
                    c1, c1, c2, c2
                );
            }

            // Centered HP/ES label
            float pct = poolCur / (float)poolMax;
            string label = s.ShowHpPercent ? $"{(int)(pct * 100f)}%" : Humanize(poolCur);
            var sz = ImGui.CalcTextSize(label);
            var center = new Vector2(
                start.X + (barW - sz.X) * 0.5f,
                start.Y + (s.BarSize.Y - sz.Y) * 0.5f
            );

            uint shadow = ImGuiHelper.Color(new Vector4(0, 0, 0, 0.90f));
            dl.AddText(center + new Vector2(1, 0), shadow, label);
            dl.AddText(center + new Vector2(-1, 0), shadow, label);
            dl.AddText(center + new Vector2(0, 1), shadow, label);
            dl.AddText(center + new Vector2(0, -1), shadow, label);
            dl.AddText(center, ImGuiHelper.Color(Vector4.One), label);
        }

        private static string Humanize(int v)
        {
            if (v >= 1_000_000_000) return $"{v / 1_000_000_000f:0.##}B";
            if (v >= 1_000_000) return $"{v / 1_000_000f:0.##}M";
            if (v >= 1_000) return $"{v / 1_000f:0.#}K";
            return v.ToString();
        }

        private static string HumanizeNumber(long v)
        {
            if (v >= 1_000_000_000L) return $"{v / 1_000_000_000f:0.##}B";
            if (v >= 1_000_000L) return $"{v / 1_000_000f:0.##}M";
            if (v >= 1_000L) return $"{v / 1_000f:0.#}K";
            return v.ToString();
        }

        private static Vector2 MeasureText(string text, float scale)
        {
            ImGui.SetWindowFontScale(scale);
            var v = ImGui.CalcTextSize(text);
            ImGui.SetWindowFontScale(1f);
            return v;
        }

        private static float MeasureTextWidth(string text, float scale)
        {
            return MeasureText(text, scale).X;
        }

        private static string EllipsizeToWidth(string text, float maxWidth, float scale)
        {
            if (maxWidth <= 0) return "";
            if (MeasureTextWidth(text, scale) <= maxWidth) return text;

            const string ell = "…";
            int lo = 0, hi = text.Length;
            string best = "";
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                string t = text.Substring(0, Math.Max(0, mid)) + ell;
                float tw = MeasureTextWidth(t, scale);
                if (tw <= maxWidth) { best = t; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        private static Vector4 HashToColor(string s, float alpha)
        {
            uint h = 2166136261;
            foreach (char c in s.ToUpperInvariant())
            {
                h ^= c;
                h *= 16777619;
            }
            float hue = (h % 360) / 360f;
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
            r = F(0); g = F(8); b = F(4);
        }

        private static List<BuffInfo> ExtractBuffs(Entity e, AuraTrackerSettings s)
        {
            var map = new Dictionary<string, (int stacks, float? dur)>();

            try
            {
                if (!e.TryGetComponent<Buffs>(out var comp, true) || comp == null)
                    return new List<BuffInfo>();

                foreach (var kv in comp.StatusEffects.ToArray())
                {
                    string cleaned = CleanBuffName(kv.Key);
                    if (string.IsNullOrEmpty(cleaned))
                        continue;

                    int stacks = Math.Max(1, (int)kv.Value.Charges);
                    float timeLeft = kv.Value.TimeLeft;
                    float total = kv.Value.TotalTime;

                    bool timeLeftFinite = !(float.IsNaN(timeLeft) || float.IsInfinity(timeLeft));
                    bool totalFinite = !(float.IsNaN(total) || float.IsInfinity(total));
                    float? dur = (s.ShowDurations && timeLeftFinite && totalFinite && timeLeft > 0f) ? timeLeft : (float?)null;

                    if (map.TryGetValue(cleaned, out var prev))
                    {
                        int newStacks = prev.stacks + stacks;
                        float? newDur = prev.dur.HasValue && dur.HasValue ? MathF.Max(prev.dur.Value, dur.Value)
                                    : prev.dur ?? dur;
                        map[cleaned] = (newStacks, newDur);
                    }
                    else
                    {
                        map[cleaned] = (stacks, dur);
                    }
                }
            }
            catch { }

            var list = new List<BuffInfo>(map.Count);
            foreach (var kv in map)
            {
                list.Add(new BuffInfo
                {
                    Name = kv.Key,
                    Stacks = Math.Max(1, kv.Value.stacks),
                    DurationSeconds = kv.Value.dur,
                    Display = null,
                    ChipWidth = 0f,
                    ChipHeight = 0f
                });
            }
            return list;
        }

        private IEnumerator<Wait> OnAreaChange()
        {
            for (; ; )
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                smoothPositions.Clear();
                dpsStates.Clear();
            }
        }

        private struct BuffInfo
        {
            public string Name;
            public int Stacks;
            public float? DurationSeconds;
            public string Display;
            public float ChipWidth;
            public float ChipHeight;
        }
    }
}
