using UnityEditor;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Integrate BaseV2 Level: the main building scene built by the Sky Island core (Tools / Locations /
    /// Location Builder -> BaseV2) as a networked level. Since LOCATIONS_UPDATE (L4.1) the work is done by
    /// LocationLevelBuilder with the BaseV2 profile (LocationProfiles.BaseV2): NetworkObject + SkyIslandLocationHost on the
    /// root, a NetworkDoor on the 19 doors, zones on every part but the shutter curtains, furniture on Interact, the fall
    /// reset, four spawn points in the yard, network manager, the player camera look (PS1 frame included). Run it again
    /// after every BaseV2 build; the scene goes last in Build Settings.
    /// </summary>
    public static class BaseV2LevelBuilder
    {
        public const string ScenePath = "Assets/Locations/BaseV2/Scenes/BaseV2.unity";

        [MenuItem("Tools/Snow/Integrate BaseV2 Level")]
        public static void IntegrateMenu() => LocationLevelBuilder.Menu(LocationProfiles.BaseV2);

        public static void Integrate() => LocationLevelBuilder.Integrate(LocationProfiles.BaseV2);
    }
}
