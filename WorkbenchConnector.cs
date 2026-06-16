using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCGWorkbenchPresets
{
    internal class WorkbenchConnector
    {
        private readonly ConfigEntry<string> _uiTypeName;
        private readonly ConfigEntry<string> _minSliderField;
        private readonly ConfigEntry<string> _maxSliderField;
        private readonly ConfigEntry<string> _keywords;
        private readonly ManualLogSource _log;

        private Type   _cachedType;
        private string _cachedTypeName;

        // Cached slider references set once via TryCacheSliders(); used by ApplyPreset.
        internal Slider CachedMinSlider { get; private set; }
        private  Slider _cachedMaxSlider;
        private  bool   _sliderLogged;

        internal WorkbenchConnector(
            ConfigEntry<string> uiTypeName,
            ConfigEntry<string> minSliderField,
            ConfigEntry<string> maxSliderField,
            ConfigEntry<string> keywords,
            ManualLogSource log)
        {
            _uiTypeName     = uiTypeName;
            _minSliderField = minSliderField;
            _maxSliderField = maxSliderField;
            _keywords       = keywords;
            _log            = log;
        }

        internal bool IsWired() =>
            !string.IsNullOrEmpty(_uiTypeName.Value)
            && !string.IsNullOrEmpty(_minSliderField.Value)
            && !string.IsNullOrEmpty(_maxSliderField.Value);

        // Called every frame until sliders are cached. Returns true on first successful cache.
        internal bool TryCacheSliders()
        {
            if (CachedMinSlider != null) return false;
            if (!IsWired()) return false;

            var type = FindWorkbenchType();
            if (type == null) return false;

            var inst = Resources.FindObjectsOfTypeAll(type).Cast<MonoBehaviour>()
                                 .FirstOrDefault(mb => mb != null);
            if (inst == null) return false;

            CachedMinSlider  = GetSlider(type, inst, _minSliderField.Value);
            _cachedMaxSlider = GetSlider(type, inst, _maxSliderField.Value);

            if (CachedMinSlider != null && !_sliderLogged)
            {
                _log.LogInfo($"[Connector] Min slider cached: '{BuildPath(CachedMinSlider.transform)}'");
                _sliderLogged = true;
                return true;
            }
            return false;
        }

        internal void ApplyPreset(float min, float max)
        {
            if (!IsWired()) { _log.LogWarning("Config not wired — run discovery (F8) first."); return; }

            // Use cached sliders on the hot path; fall back to full lookup if cache is stale.
            var minSlider = CachedMinSlider;
            var maxSlider = _cachedMaxSlider;

            if (minSlider == null || maxSlider == null)
            {
                var type = FindWorkbenchType();
                if (type == null) { _log.LogWarning($"UI type '{_uiTypeName.Value}' not found."); return; }
                var inst = Resources.FindObjectsOfTypeAll(type).Cast<Component>()
                    .FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy);
                if (inst == null) { _log.LogWarning($"No active '{_uiTypeName.Value}' — is the workbench open?"); return; }
                minSlider = GetSlider(type, inst, _minSliderField.Value);
                maxSlider = GetSlider(type, inst, _maxSliderField.Value);
            }

            if (minSlider == null || maxSlider == null)
            {
                _log.LogWarning($"Could not resolve sliders '{_minSliderField.Value}'/'{_maxSliderField.Value}'.");
                return;
            }

            minSlider.value = Mathf.Clamp(min, minSlider.minValue, minSlider.maxValue);
            maxSlider.value = Mathf.Clamp(max, maxSlider.minValue, maxSlider.maxValue);
            _log.LogInfo($"Preset applied: min={min} max={max}");
        }

        internal void DumpWorkbenchUI()
        {
            _log.LogInfo($"F8 received — scanning for workbench UI (keywords: {_keywords.Value})...");
            var keys = _keywords.Value.Split(',')
                .Select(k => k.Trim().ToLowerInvariant())
                .Where(k => k.Length > 0).ToArray();

            var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(mb => mb != null && mb.isActiveAndEnabled && mb.gameObject.activeInHierarchy)
                .ToList();

            var hits = all.Where(mb => MatchesKeyword(mb.gameObject, keys))
                          .Select(mb => mb.GetType()).Distinct().ToList();

            if (hits.Count == 0)
            {
                _log.LogWarning("No keyword match — falling back to full scan of active components with Slider fields...");
                hits = all.Select(mb => mb.GetType()).Distinct()
                    .Where(t =>
                        t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Any(f => f.FieldType == typeof(Slider))
                        || t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .Any(p => p.PropertyType == typeof(Slider)))
                    .ToList();
            }

            if (hits.Count == 0)
            {
                _log.LogWarning("No components with Slider fields found. Make sure the bundle panel is OPEN when pressing F8.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("===== WORKBENCH UI DUMP =====");
            foreach (var t in hits)
            {
                sb.AppendLine($"\nComponent type: {t.FullName}   (candidate WorkbenchUITypeName)");
                foreach (var inst in Resources.FindObjectsOfTypeAll(t).Cast<UnityEngine.Object>())
                {
                    var comp = inst as Component;
                    if (comp == null || !comp.gameObject.activeInHierarchy) continue;
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var val = f.GetValue(comp);
                        if (val is Slider s)
                            sb.AppendLine($"    Slider   field='{f.Name}'  value={s.value}  min={s.minValue}  max={s.maxValue}");
                        else if (val is TMP_InputField tif)
                            sb.AppendLine($"    InputFld field='{f.Name}'  text='{tif.text}'");
                        else if (val is TMP_Dropdown td)
                            sb.AppendLine($"    Dropdown field='{f.Name}'  value={td.value}");
                    }
                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object pval;
                        try { pval = p.GetValue(comp); } catch { continue; }
                        if (pval is Slider sp)
                            sb.AppendLine($"    Slider   prop='{p.Name}'  value={sp.value}  min={sp.minValue}  max={sp.maxValue}");
                    }
                    break;
                }
            }
            _log.LogInfo(sb.ToString());
        }

        private Type FindWorkbenchType()
        {
            if (_cachedType != null && _cachedTypeName == _uiTypeName.Value)
                return _cachedType;
            _cachedTypeName = _uiTypeName.Value;
            _cachedType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeTypes)
                .FirstOrDefault(t => t.Name == _uiTypeName.Value || t.FullName == _uiTypeName.Value);
            return _cachedType;
        }

        private static Slider GetSlider(Type type, object inst, string memberName)
        {
            var f = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(inst) as Slider;
            var p = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(inst) as Slider;
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        private static bool MatchesKeyword(GameObject go, string[] keys)
        {
            var t = go.transform;
            while (t != null)
            {
                if (keys.Any(k => t.name.ToLowerInvariant().Contains(k))) return true;
                t = t.parent;
            }
            return false;
        }

        private static string BuildPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
