using System;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The four data files of the Forest opening (Assets/Locations/Forest/Data, FOREST_U3 appendix C) as JsonUtility reads
    /// them: every list is an array of objects, the field name is the key. Components keep the files as TextAssets and
    /// read them in play (F5.1.2: the core's SkyIslandJson is editor-only). Absent numbers stay at -1.
    /// </summary>
    public static class ForestData
    {
        public static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        public static Quaternion Q(float[] a) =>
            a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        public static T Read<T>(TextAsset asset) where T : class =>
            asset != null && !string.IsNullOrEmpty(asset.text) ? JsonUtility.FromJson<T>(asset.text) : null;
    }

    [Serializable] public class ForestBox { public float[] center, size; }
    [Serializable] public class ForestPoint { public float[] p; }
    [Serializable] public class ForestOrientedBox { public float[] p, q, size; }

    // ------------------------------------------------------------------ Forest_Opening.json
    [Serializable]
    public class OpeningData
    {
        public OpeningItem[] items;
        public OpeningInteractable[] interactables;
        public OpeningLock[] locks;
        public OpeningZone[] zones;
        public ForestOrientedRoom[] rooms;
        public OpeningSleep sleep;
        public OpeningStep[] steps;
        public OpeningScare[] scares;
    }

    [Serializable]
    public class OpeningItem
    {
        public string id, model, readable, key, lockedWith;
        public float[] p, q;
        public ForestBox collider;
        public float massKg = -1f, fuelL = -1f, capacityL = -1f;
    }

    [Serializable]
    public class OpeningInteractable
    {
        public string id, model, child, kind;
        public float[] nozzle, neck, stand, operatorPos;
    }

    [Serializable] public class OpeningLock { public string part, key, by; }

    [Serializable]
    public class OpeningZone
    {
        public string id, kind, label;
        public float[] p, q, size;
    }

    [Serializable]
    public class ForestOrientedRoom
    {
        public string name;
        public float[] p, q, size;
    }

    [Serializable]
    public class OpeningSleep
    {
        public string room;
        public ForestPoint[] spots;
    }

    [Serializable] public class OpeningStep { public string id; }

    [Serializable]
    public class OpeningScare
    {
        public string id, room, lightSwitch, door, shutterSwitch;
        public float[] p, bell;
        public ForestOrientedBox box;
        public ForestPoint[] soundPath;
        public string[] rooms;
        public ForestDecal[] decals;
    }

    [Serializable]
    public class ForestDecal
    {
        public string id, room, label, kind, material;
        public float[] uv, p, q;
        public float size;
        public bool clean;
    }

    // ------------------------------------------------------------------ Forest_Dressing.json
    [Serializable]
    public class DressingData
    {
        public DressingProp[] props, notes;
        public ForestDecal[] decals;
    }

    [Serializable]
    public class DressingProp
    {
        public string id, model, room, readable;
        public float[] p, q;
        public ForestBox collider;
        public float massKg = -1f;
    }

    // ------------------------------------------------------------------ Forest_Notes.json
    [Serializable] public class NotesData { public NoteText[] notes; }

    [Serializable] public class NoteText { public string id, title, author, date, de, ru; }

    // ------------------------------------------------------------------ Forest_Items.json
    [Serializable] public class ItemsData { public ItemEntry[] items; }

    [Serializable]
    public class ItemEntry
    {
        public string model, label, itemKey, kind, key;
        public float massKg;
        public ForestBox collider;
        public bool clean, readable;
        public ItemLight light;
    }

    [Serializable]
    public class ItemLight
    {
        public float[] p, d;
        public string lensOn, lensOff;
    }
}
