using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Snow.Tests
{
    /// <summary>
    /// AT-HL2 "keys do not conflict", INPUT-02 separate maps: inside one action map no control path drives two actions,
    /// and E means "use" on foot but yaw in a seat only because OnFoot and Seat are never enabled together.
    /// </summary>
    public class InputBindingsTests
    {
        const string AssetPath = "Assets/Snow/Input/SnowControls.inputactions";

        static InputActionAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.That(asset, Is.Not.Null, AssetPath);
            return asset;
        }

        [Test]
        public void ThreeSeparateMaps()
        {
            var names = Load().actionMaps.Select(m => m.name).ToArray();
            Assert.That(names, Is.EquivalentTo(new[] { "OnFoot", "Seat", "Cursor" }));
        }

        [Test]
        public void NoPathBoundToTwoActionsInOneMap()
        {
            foreach (var map in Load().actionMaps)
            {
                var owner = new Dictionary<string, string>();
                foreach (var b in map.bindings)
                {
                    if (b.isComposite || string.IsNullOrEmpty(b.path)) continue;
                    if (owner.TryGetValue(b.path, out string other) && other != b.action)
                        Assert.Fail($"{map.name}: {b.path} drives both {other} and {b.action}");
                    owner[b.path] = b.action;
                }
            }
        }

        [Test]
        public void TemporaryLayoutMatchesSpec()
        {
            var a = Load();
            string Paths(string map, string action) =>
                string.Join(",", a.FindActionMap(map).bindings.Where(b => b.action == action && !b.isComposite).Select(b => b.path));
            Assert.That(Paths("OnFoot", "Use"), Is.EqualTo("<Keyboard>/e"), "§3: E = use on foot");
            Assert.That(Paths("Seat", "UseExit"), Is.EqualTo("<Keyboard>/f"), "§3: F = use, hold F = exit in a seat");
            Assert.That(Paths("Seat", "Yaw"), Does.Contain("<Keyboard>/q").And.Contain("<Keyboard>/e"), "INPUT-01: Q/E yaw");
            Assert.That(Paths("Seat", "Pitch"), Does.Contain("<Keyboard>/w").And.Contain("<Keyboard>/s"));
            Assert.That(Paths("Seat", "Roll"), Does.Contain("<Keyboard>/a").And.Contain("<Keyboard>/d"));
            Assert.That(Paths("Seat", "Throttle"), Does.Contain("<Keyboard>/leftShift").And.Contain("<Keyboard>/leftCtrl"));
        }
    }
}
