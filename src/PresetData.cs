using System.Collections.Generic;

namespace BetterPresets;

public sealed class PresetStore
{
    public int version = 1;
    public List<PresetData> presets = new List<PresetData>();
}

public sealed class PresetData
{
    public string name = "";
    public int startingWeaponId;
    public string playerCostume = "PinkRabbit";
    public string playerCostumeSkin = "";
    public List<int> favoriteItemIds = new List<int>();
    public Dictionary<string, int> passivePoints = new Dictionary<string, int>();
    public int dimensionPocketCount;
    public List<PocketItem> dimensionPocketItems = new List<PocketItem>();
    public int fruitSkewerAdaptiveItemDropBonus = 1;
    public int fruitSkewerFruitCount;
    public List<FruitEntry> fruits = new List<FruitEntry>();
    public int uiCachedDimensionPocketStorage = -1;
    public int uiCachedFruitSkewerAdditionalCount = -1;
}

public sealed class PocketItem
{
    public int instanceId = -1;
    public int entityId = -1;
    public int quantity = 1;
}

public sealed class FruitEntry
{
    public string category = "";
    public int value;
}
