using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using Newtonsoft.Json;

namespace AuraTracker
{
    public sealed class AuraTracker : PCore<AuraTrackerSettings>
    {
        private const string PluginVersion = "1.3.7";

        private readonly DpsTracker dpsTracker = new();
        private readonly MonsterCollector monsterCollector = new();
        private readonly PanelRenderer panelRenderer = new();
        private readonly SettingsUiRenderer settingsRenderer = new(PluginVersion);
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

            this.onAreaChange = CoroutineHandler.Start(OnAreaChange(), string.Empty, 0);
        }

        public override void OnDisable()
        {
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
            this.dpsTracker.Reset();
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            this.settingsRenderer.Draw(this.Settings);
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState && Core.States.GameCurrentState != GameStateTypes.EscapeState)
            {
                return;
            }

            var inGame = Core.States.InGameStateObject;
            if (!this.Settings.DrawWhenGameInBackground && !Core.Process.Foreground)
            {
                return;
            }

            if (inGame.GameUi.SkillTreeNodesUiElements.Count > 0)
            {
                return;
            }

            var overlaySize = new Vector2(Core.Overlay.Size.Width, Core.Overlay.Size.Height);
            var overlayCenter = overlaySize * 0.5f;

            List<MonsterCollector.MonsterSnapshot> monsters = this.monsterCollector.Collect(this.Settings, inGame, overlayCenter);
            if (monsters.Count == 0)
            {
                return;
            }

            this.panelRenderer.Render(monsters, this.Settings, this.dpsTracker, overlaySize);
        }

        private IEnumerator<Wait> OnAreaChange()
        {
            for (; ; )
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.dpsTracker.Reset();
            }
        }
    }
}
