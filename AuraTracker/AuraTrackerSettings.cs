using System.Numerics;
using GameHelper.Plugin;
using GameHelper.RemoteEnums; // Rarity

namespace AuraTracker
{
    public sealed class AuraTrackerSettings : IPSettings
    {
        // General / filters
        public bool DrawWhenGameInBackground = false;

        public float ScreenRangePx = 1800;    // “nearby” in screen space
        public int MaxEnemies = 8;

        // Show this rarity and above
        public Rarity MinRarityToShow = Rarity.Magic;

        // Left list anchor & spacing (requested defaults)
        public Vector2 LeftAnchor = new(250f, 120f);
        public float EntrySpacing = 16f;
        public float BarToBuffSpacing = 3f;
        public float MaxListHeight = 0f; // 0 = use overlay height

        // Content width: fixed (no auto). Default 300.
        public float PanelWidth = 300f;

        // Panel visuals
        public bool ShowPanelBackground = true;
        public Vector4 PanelBg = new(0f, 0f, 0f, 0.35f);
        public Vector4 PanelBorder = new(0f, 0f, 0f, 0.8f);
        public Vector2 PanelPadding = new(8f, 8f);
        public float PanelCornerRadius = 6f;
        public float PanelRightSafeMargin = 24f;

        // Fancy visuals
        public bool FancyPanelShadow = true;
        public bool FancyRarityStripe = true;
        public bool FancyBarGloss = true;
        public bool FancyBarInnerBorder = true;
        public bool FancyEsDivider = true;
        public bool FancyChipGloss = true;

        // Style amounts
        public float PanelShadowSize = 10f;   // px
        public float PanelShadowAlpha = 0.25f;

        public float ChipCornerRadius = 6f;
        public float ChipShadowAlpha = 0.25f;
        public float ChipGlossAlpha = 0.25f;

        public float BarCornerRadius = 5f;
        public float BarInnerBorderAlpha = 0.35f;
        public float EsDividerAlpha = 0.75f;

        // Bar (width always follows panel width; Y = height)
        public Vector4 BarBg = new(0f, 0f, 0f, 0.5f);
        public Vector4 BarHpFill = new(1f, 0.35f, 0.2f, 1f);   // HP segment color
        public Vector4 BarEsFill = new(0f, 1f, 1f, 1f);        // ES segment color
        public Vector2 BarSize = new(180f, 18f);             // slightly taller by default
        public bool ShowHpPercent = false;                  // toggle percent vs absolute

        // Buff chips
        public float BuffPad = 2f;
        public int MaxBuffsPerEnemy = 12;
        public float BuffBgAlpha = 0.35f;
        public float BuffTextScale = 1.0f;
        public bool ShowDurations = true; // finite only; indefinite/infinite hidden
    }
}
