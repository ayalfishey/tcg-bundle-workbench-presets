using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCGWorkbenchPresets
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid    = "com.yourname.tcgworkbenchpresets";
        public const string Name    = "Workbench Presets";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private WorkbenchConnector _connector;
        private PresetOverlay      _overlay;
        private PresetDefinition[] _presets;
        private ConfigEntry<KeyCode> _dumpKey;

        private void Awake()
        {
            Log = Logger;

            var dumpKeyEntry = Config.Bind("1. Discovery", "DumpKey", KeyCode.F8,
                "Press over an OPEN workbench panel to log its UI type, slider field names, and slider ranges.");
            _dumpKey = dumpKeyEntry;

            var keywords    = Config.Bind("1. Discovery", "NameKeywords", "workbench,bulk,bundle,price,box",
                "Comma-separated, case-insensitive substrings used to spot the workbench UI GameObject.");
            var uiTypeName  = Config.Bind("2. Wiring", "WorkbenchUITypeName", "",
                "Class name of the workbench UI (read it off the discovery dump).");
            var minField    = Config.Bind("2. Wiring", "MinSliderField", "",
                "Field name of the Minimum Price slider (from the dump).");
            var maxField    = Config.Bind("2. Wiring", "MaxSliderField", "",
                "Field name of the Below Max Price slider (from the dump).");

            _presets = new[]
            {
                new PresetDefinition("White",
                    Color.white,
                    Config.Bind("3. Presets", "WhiteKey", KeyCode.Alpha1, "Hotkey: white ($0.01-$0.99)."),
                    Config.Bind("3. Presets", "WhiteMin",    1f, "Slider cents: $0.01"),
                    Config.Bind("3. Presets", "WhiteMax",   99f, "Slider cents: $0.99")),
                new PresetDefinition("Yellow",
                    Color.yellow,
                    Config.Bind("3. Presets", "YellowKey", KeyCode.Alpha2, "Hotkey: yellow ($1.00-$1.99)."),
                    Config.Bind("3. Presets", "YellowMin", 100f, "Slider cents: $1.00"),
                    Config.Bind("3. Presets", "YellowMax", 199f, "Slider cents: $1.99")),
                new PresetDefinition("Purple",
                    new Color(0.6f, 0.2f, 0.85f),
                    Config.Bind("3. Presets", "PurpleKey", KeyCode.Alpha3, "Hotkey: purple ($2.00-$3.99)."),
                    Config.Bind("3. Presets", "PurpleMin", 200f, "Slider cents: $2.00"),
                    Config.Bind("3. Presets", "PurpleMax", 399f, "Slider cents: $3.99")),
                new PresetDefinition("Red",
                    new Color(0.9f, 0.2f, 0.2f),
                    Config.Bind("3. Presets", "RedKey",    KeyCode.Alpha4, "Hotkey: red ($4.00-$11.99)."),
                    Config.Bind("3. Presets", "RedMin",    400f, "Slider cents: $4.00"),
                    Config.Bind("3. Presets", "RedMax",   1199f, "Slider cents: $11.99")),
                new PresetDefinition("Green",
                    new Color(0.1f, 0.7f, 0.15f),
                    Config.Bind("3. Presets", "GreenKey",  KeyCode.Alpha5, "Hotkey: green ($12.00-$20.00)."),
                    Config.Bind("3. Presets", "GreenMin", 1200f, "Slider cents: $12.00"),
                    Config.Bind("3. Presets", "GreenMax", 2000f, "Slider cents: $20.00")),
            };

            var btnHeight     = Config.Bind("4. Buttons", "ButtonHeight",   50f,  "Height of each preset button in pixels.");
            var barLeftFrac   = Config.Bind("4. Buttons", "BarLeftFrac",   0.63f,
                "Left edge of the button bar as a fraction of screen width (0.0=left, 1.0=right). Default 0.63.");
            var barBottomFrac = Config.Bind("4. Buttons", "BarBottomFrac", 0.38f,
                "Bottom of the button bar as a fraction of screen height (0.0=bottom, 1.0=top). Increase to move up.");
            var barWidthFrac  = Config.Bind("4. Buttons", "BarWidthFrac",  0.27f,
                "Width of the button bar as a fraction of screen width. Default 0.27.");

            _connector = new WorkbenchConnector(uiTypeName, minField, maxField, keywords, Logger);
            _overlay   = new PresetOverlay(btnHeight, barLeftFrac, barBottomFrac, barWidthFrac, Logger);

            SceneManager.sceneLoaded += OnFirstSceneLoaded;

            Log.LogInfo($"{Name} {Version} loaded. Open a workbench and press {_dumpKey.Value} to discover field names.");
            if (_connector.IsWired())
                Log.LogInfo($"Plugin wired and ready. Config: {Config.ConfigFilePath}");
            else
                Log.LogWarning($"Config not yet wired — fill in section 2 of: {Config.ConfigFilePath}");
        }

        private void OnFirstSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFirstSceneLoaded;
            Log.LogInfo($"[Diag] First scene loaded: '{scene.name}'. Building overlay and spawning updater.");

            _overlay.Build(_presets, _connector);

            var updaterGo = new GameObject("WorkbenchPresetUpdater");
            updaterGo.hideFlags = HideFlags.HideAndDontSave;
            updaterGo.AddComponent<PluginUpdater>().Owner = this;
        }

        internal void PluginUpdate()
        {
            _connector.TryCacheSliders();
            _overlay.TryCacheStyle(_connector.CachedMinSlider);
            _overlay.UpdateOverlay(_connector.CachedMinSlider);

            if (Input.GetKeyDown(_dumpKey.Value))
                _connector.DumpWorkbenchUI();

            foreach (var preset in _presets)
                if (Input.GetKeyDown(preset.Key.Value))
                    _connector.ApplyPreset(preset.Min.Value, preset.Max.Value);
        }

        private class PluginUpdater : MonoBehaviour
        {
            internal Plugin Owner;
            private void Update() => Owner.PluginUpdate();
        }
    }
}
