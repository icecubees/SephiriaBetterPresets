using UnityEngine;

namespace BetterPresets;

public sealed class BetterPresetsMod : HorayModBase
{
    private GameObject controllerObject;

    protected override void OnModLoaded()
    {
        controllerObject = new GameObject("BetterPresetsBootstrap");
        Object.DontDestroyOnLoad(controllerObject);
        controllerObject.AddComponent<BetterPresetsBootstrap>();
        Debug.Log("[BetterPresets] Bootstrap loaded. Controller will initialize when the preset panel opens.");
    }

    protected override void OnModUnloaded()
    {
        if (controllerObject != null)
        {
            Object.Destroy(controllerObject);
            controllerObject = null;
        }
    }
}
