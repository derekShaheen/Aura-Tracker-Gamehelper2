using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;

namespace AuraTracker;

internal sealed class MonsterCollector
{
    internal readonly struct MonsterSnapshot
    {
        public MonsterSnapshot(Entity entity, Vector2 screenPosition, Life life, Rarity rarity, List<BuffVisuals.BuffInfo> buffs, string name, float nameWidth)
        {
            Entity = entity;
            ScreenPosition = screenPosition;
            Life = life;
            Rarity = rarity;
            Buffs = buffs;
            Name = name;
            NameWidth = nameWidth;
        }

        public Entity Entity { get; }
        public Vector2 ScreenPosition { get; }
        public Life Life { get; }
        public Rarity Rarity { get; }
        public List<BuffVisuals.BuffInfo> Buffs { get; }
        public string Name { get; }
        public float NameWidth { get; }
    }

    public List<MonsterSnapshot> Collect(AuraTrackerSettings settings, InGameState inGame, Vector2 overlayCenter)
    {
        var world = inGame.CurrentWorldInstance;
        var area = inGame.CurrentAreaInstance;

        var candidates = new List<MonsterSnapshot>();
        var seenPathWorld = new HashSet<string>();

        foreach (var kv in area.AwakeEntities)
        {
            var entity = kv.Value;

            if (!entity.IsValid || entity.EntityState == EntityStates.PinnacleBossHidden || entity.EntityState == EntityStates.Useless || entity.EntityState == EntityStates.MonsterFriendly)
            {
                continue;
            }

            if (entity.EntityType != EntityTypes.Monster)
            {
                continue;
            }

            if (entity.EntitySubtype == EntitySubtypes.PlayerOther || entity.EntitySubtype == EntitySubtypes.PlayerSelf)
            {
                continue;
            }

            if (!TryGetRarity(entity, out Rarity rarity) || rarity < settings.MinRarityToShow)
            {
                continue;
            }

            if (!entity.TryGetComponent<Render>(out var render, true))
            {
                continue;
            }

            var worldRaw = render.WorldPosition;
            string pathKey = entity.Path ?? string.Empty;
            string key = $"{pathKey}|{BitConverter.SingleToInt32Bits(worldRaw.X)},{BitConverter.SingleToInt32Bits(worldRaw.Y)},{BitConverter.SingleToInt32Bits(worldRaw.Z)}";
            if (!seenPathWorld.Add(key))
            {
                continue;
            }

            var position = render.WorldPosition;
            position.Z -= render.ModelBounds.Z;
            var screen = world.WorldToScreen(position, position.Z);

            if (Vector2.Distance(screen, overlayCenter) > settings.ScreenRangePx)
            {
                continue;
            }

            if (!entity.TryGetComponent<Life>(out var life, true))
            {
                continue;
            }

            var buffs = BuffVisuals.Extract(entity, settings);
            BuffVisuals.PopulateDisplayData(buffs, settings);

            string name = GetMonsterName(entity) ?? "Unknown";
            float nameWidth = ImGuiTextUtil.MeasureWidth(name, 1.0f);

            candidates.Add(new MonsterSnapshot(entity, screen, life, rarity, buffs, name, nameWidth));
        }

        candidates = candidates
            .GroupBy(t => t.Entity.Id)
            .Select(g => g.First())
            .ToList();

        var selected = new List<MonsterSnapshot>();
        var usedIds = new HashSet<uint>();
        int slots = Math.Max(0, settings.MaxEnemies);
        Rarity[] order = { Rarity.Unique, Rarity.Rare, Rarity.Magic, Rarity.Normal };

        foreach (var rarity in order)
        {
            if (slots <= 0)
            {
                break;
            }

            foreach (var item in candidates.Where(t => t.Rarity == rarity).OrderBy(t => Vector2.Distance(t.ScreenPosition, overlayCenter)))
            {
                if (slots <= 0)
                {
                    break;
                }

                if (!usedIds.Add(item.Entity.Id))
                {
                    continue;
                }

                selected.Add(item);
                slots--;
            }
        }

        return selected
            .OrderByDescending(t => t.Rarity)
            .ThenBy(t => t.Entity.Id)
            .ToList();
    }

    private static bool TryGetRarity(Entity entity, out Rarity rarity)
    {
        rarity = Rarity.Normal;
        try
        {
            if (entity.TryGetComponent<ObjectMagicProperties>(out var omp, true))
            {
                rarity = omp.Rarity;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string GetMonsterName(Entity entity)
    {
        try
        {
            string path = entity.Path;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string tail = (slash >= 0) ? path[(slash + 1)..] : path;

            int at = tail.IndexOf('@');
            if (at >= 0)
            {
                tail = tail[..at];
            }

            tail = tail.Replace('_', ' ').Trim();
            if (tail.Length == 0)
            {
                return null;
            }

            var builder = new StringBuilder(tail.Length * 2);
            builder.Append(tail[0]);
            for (int i = 1; i < tail.Length; i++)
            {
                char c = tail[i];
                if (char.IsUpper(c))
                {
                    builder.Append(' ');
                }

                builder.Append(c);
            }

            string spaced = builder.ToString().Trim();

            if (spaced.Length > 0 && char.IsLetter(spaced[0]))
            {
                spaced = char.ToUpperInvariant(spaced[0]) + (spaced.Length > 1 ? spaced[1..] : string.Empty);
            }

            spaced = Regex.Replace(spaced, "\\s*\\d+$", string.Empty).Trim();
            return spaced;
        }
        catch
        {
            return null;
        }
    }
}
