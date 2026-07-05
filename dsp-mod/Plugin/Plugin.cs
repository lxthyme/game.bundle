using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DspBlueprintTransform.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BlueprintTransformPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lxthyme.dspblueprinttransform";
        public const string PluginName = "DSP Blueprint Transform";
        public const string PluginVersion = "0.1.0";

        private ConfigEntry<KeyboardShortcut> _toggleKey = null!;
        private ConfigEntry<int> _windowWidth = null!;
        private ConfigEntry<int> _windowHeight = null!;
        private TransformWindow _window = null!;

        private void Awake()
        {
            _toggleKey = Config.Bind(
                "General",
                "ToggleWindowKey",
                new KeyboardShortcut(KeyCode.F7),
                "打开/关闭蓝图变换窗口的快捷键");

            _windowWidth = Config.Bind(
                "General",
                "WindowWidth",
                420,
                "窗口宽度");

            _windowHeight = Config.Bind(
                "General",
                "WindowHeight",
                560,
                "窗口高度");

            _window = gameObject.AddComponent<TransformWindow>();
            _window.Init(_windowWidth, _windowHeight);
            _window.enabled = false;

            Logger.LogInfo($"{PluginName} v{PluginVersion} 已加载");
        }

        private void Update()
        {
            if (_toggleKey.Value.IsDown())
                _window.enabled = !_window.enabled;
        }
    }
}
