using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// What the Forest opening needs to know about an item prefab (Forest_Items.json, written by Tools / Snow / Forest /
    /// Build Item Prefabs): the model, whether it is rubbish for the bag, the lock label of a key.
    /// </summary>
    public class ForestItemTag : MonoBehaviour
    {
        [Tooltip("Model name (Item_Jerrycan20)")] public string model;
        [Tooltip("Rubbish: the trash bag takes it (Forest_Items.json clean)")] public bool clean;
        [Tooltip("Keys: the lock label (Schranke, Hauptgebäude, Garage, Generator); empty for other items")] public string key;
    }
}
