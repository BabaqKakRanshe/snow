using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Van / Build Old Van Variant (WORLD_CONTRACT.md, «Старый запасной фургон»; pack source/vehicles/van3_old):
    /// the old spare van the station chief sends later in the story — a Prefab Variant of the package's van
    /// (Assets/Van/Prefabs/Van.prefab, Van_Unity_v1) with the body material M_VanOld_Body on T_VanOld_Body_BaseColor (old army
    /// khaki, rust) instead of M_Van_Body, and without the winch: Winch_Drum, Winch_Clutch, Winch_Hook and the points
    /// Hand_Winch_Clutch, Winch_Fairlead, Hand_Winch_Hook are removed. Not placed in any scene. Re-running rewrites the same
    /// assets (same GUIDs).
    /// </summary>
    public static class VanOldBuilder
    {
        const string BasePrefab = "Assets/Van/Prefabs/Van.prefab";
        const string VariantPath = "Assets/Van/Prefabs/VanOld.prefab";
        const string BodyMaterial = "Assets/Van/Materials/M_Van_Body.mat";
        const string OldMaterial = "Assets/Van/Materials/M_VanOld_Body.mat";
        const string BodyTexture = "Assets/Van/Textures/T_Van_Body_BaseColor.png";
        const string OldTexture = "Assets/Van/Textures/T_VanOld_Body_BaseColor.png";
        // the winch's parts, its points and the interaction zones of the package on them
        static readonly string[] Removed = { "Winch_Drum", "Winch_Clutch", "Winch_Hook", "Hand_Winch_Clutch", "Winch_Fairlead", "Hand_Winch_Hook", "IV_Winch_Clutch", "IV_Winch_Hook" };

        [MenuItem("Tools/Snow/Van/Build Old Van Variant")]
        public static bool Build()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
            var body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterial);
            if (basePrefab == null || body == null) { Debug.LogError($"[Snow] Old van: {BasePrefab} or {BodyMaterial} missing."); return false; }

            // the texture imports like the van's own body texture
            if (AssetImporter.GetAtPath(OldTexture) is TextureImporter ti && AssetImporter.GetAtPath(BodyTexture) is TextureImporter src)
            {
                ti.textureType = src.textureType;
                ti.textureShape = src.textureShape;
                ti.filterMode = src.filterMode;
                ti.mipmapEnabled = src.mipmapEnabled;
                ti.textureCompression = src.textureCompression;
                ti.wrapMode = src.wrapMode;
                ti.npotScale = src.npotScale;
                ti.sRGBTexture = src.sRGBTexture;
                ti.maxTextureSize = src.maxTextureSize;
                ti.alphaIsTransparency = src.alphaIsTransparency;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(OldTexture);
            if (tex == null) { Debug.LogError($"[Snow] Old van: {OldTexture} missing."); return false; }

            var old = AssetDatabase.LoadAssetAtPath<Material>(OldMaterial);
            if (old == null)
            {
                old = new Material(body) { name = "M_VanOld_Body" };
                AssetDatabase.CreateAsset(old, OldMaterial);
            }
            else old.CopyPropertiesFromMaterial(body);
            old.SetTexture("_BaseMap", tex);
            if (old.HasProperty("_MainTex")) old.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(old);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                inst.name = "VanOld";
                int swapped = 0;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    bool any = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == body) { mats[i] = old; any = true; }
                    if (!any) continue;
                    r.sharedMaterials = mats;
                    swapped++;
                }
                int removed = 0;
                foreach (string name in Removed)
                    foreach (Transform t in inst.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray())
                    {
                        if (t == null) continue;
                        Object.DestroyImmediate(t.gameObject);         // a removed-GameObject override of the variant
                        removed++;
                    }
                PrefabUtility.SaveAsPrefabAsset(inst, VariantPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Snow] Old van: {VariantPath} (variant of {BasePrefab}): body material on {swapped} renderers, {removed} winch parts / points removed" +
                          (removed < Removed.Length ? $" (of {Removed.Length}: some names not found)" : "") + ".");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Snow] Old van: {e.Message}");
                return false;
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
