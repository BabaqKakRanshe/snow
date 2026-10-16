using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    /// <summary>
    /// Tools > Locations > Location Builder: every location of the project (folders with Data/&lt;Id&gt;_Layout.json,
    /// Sky Island included) with Build, Open Scene and Capture buttons.
    /// </summary>
    public class SkyIslandLocationWindow : EditorWindow
    {
        List<(string id, string root)> _locations = new List<(string id, string root)>();
        Vector2 _scroll;

        [MenuItem("Tools/Locations/Location Builder", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<SkyIslandLocationWindow>();
            w.titleContent = new GUIContent("Location Builder");
            w.minSize = new Vector2(440f, 220f);
            w.Refresh();
            w.Show();
        }

        void OnEnable()
        {
            Refresh();
        }

        void OnProjectChange()
        {
            Refresh();
            Repaint();
        }

        void Refresh()
        {
            try { _locations = SkyIslandBuilder.FindLocations(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkyIsland] location list: " + ex.Message);
                _locations = new List<(string id, string root)>();
            }
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_locations.Count == 1 ? "1 location" : _locations.Count + " locations", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f))) Refresh();
            }
            if (_locations.Count == 0)
            {
                EditorGUILayout.HelpBox("No location found. A location is a folder with Data/<Id>_Layout.json, _Materials.json and _Look.json " +
                                        "(Assets/Locations/<Id>, imported from its package).", MessageType.Info);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var (id, root) in _locations) DrawLocation(id, root);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Build: materials, prefabs and <folder>/Scenes/<Id>.unity.  Capture: renders every viewpoint of the scene " +
                                    "into <project>/SkyIslandCaptures/<Id> with a report against the previews.", MessageType.None);
        }

        static void DrawLocation(string id, string root)
        {
            string scene = SkyIslandBuilder.LocationScenePath(root, id);
            bool hasScene = File.Exists(scene);
            bool isOpen = EditorSceneManager.GetActiveScene().path == scene;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(id, EditorStyles.boldLabel, GUILayout.Width(110f));
                    GUILayout.Label(root, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(isOpen ? "open" : (hasScene ? "built" : "not built"), EditorStyles.miniLabel);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(hasScene ? "Rebuild" : "Build"))
                        Defer(() => { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) SkyIslandBuilder.BuildLocationAt(root, id); });
                    using (new EditorGUI.DisabledScope(!hasScene))
                    {
                        if (GUILayout.Button("Open Scene"))
                            Defer(() => { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single); });
                        if (GUILayout.Button("Capture"))
                            Defer(() => SkyIslandBuilder.CaptureLocationAt(root, id));
                    }
                }
            }
        }

        /// <summary>Runs after the GUI pass (building / opening scenes inside OnGUI breaks the layout).</summary>
        static void Defer(Action action)
        {
            EditorApplication.delayCall += () => action();
        }
    }
}
