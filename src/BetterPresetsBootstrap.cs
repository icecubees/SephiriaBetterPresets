using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterPresets;

public sealed class BetterPresetsBootstrap : MonoBehaviour
{
    private float nextProbeTime;
    private bool controllerAttached;
    private int loadFailureCount;

    private void Awake()
    {
        EnsurePresetFile();
    }

    private void Update()
    {
        if (controllerAttached || Time.unscaledTime < nextProbeTime)
        {
            return;
        }

        nextProbeTime = Time.unscaledTime + 0.5f;
        UI_PresetPanel panel = FindFirstObjectByType<UI_PresetPanel>();
        if (panel == null || !panel.IsOpened)
        {
            return;
        }

        AttachController();
    }

    private void AttachController()
    {
        if (controllerAttached)
        {
            return;
        }

        controllerAttached = true;
        gameObject.name = "BetterPresetsController";
        try
        {
            string folder = Path.GetDirectoryName(typeof(BetterPresetsMod).Assembly.Location);
            string corePath = Path.Combine(folder ?? "", "BetterPresets.Core.dll");
            Assembly coreAssembly = Assembly.LoadFrom(corePath);
            Type controllerType = coreAssembly.GetType("BetterPresets.BetterPresetsController", throwOnError: true);
            gameObject.AddComponent(controllerType);
            Destroy(this);
            Debug.Log("[BetterPresets] Controller initialized after preset panel opened.");
        }
        catch (Exception ex)
        {
            controllerAttached = false;
            loadFailureCount++;
            nextProbeTime = Time.unscaledTime + Mathf.Min(30f, 5f * loadFailureCount);
            Debug.LogError("[BetterPresets] Failed to load BetterPresets.Core.dll. Retrying in " + Mathf.RoundToInt(nextProbeTime - Time.unscaledTime) + "s: " + ex);
        }
    }

    private static void EnsurePresetFile()
    {
        try
        {
            string folder = Path.GetDirectoryName(typeof(BetterPresetsMod).Assembly.Location);
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "AddOns", "BetterPresets");
            }

            Directory.CreateDirectory(folder);
            string presetFile = Path.Combine(folder, "presets.json");
            if (!File.Exists(presetFile))
            {
                File.WriteAllText(presetFile, "{\r\n  \"version\": 1,\r\n  \"presets\": []\r\n}\r\n");
                Debug.Log("[BetterPresets] Created missing presets.json.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[BetterPresets] Failed to create presets.json: " + ex);
        }
    }
}
