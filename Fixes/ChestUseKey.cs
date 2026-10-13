using BepInEx.Bootstrap;
using HarmonyLib;
using System.Reflection;

namespace NhxModFixes.Fixes
{
    // Opening a chest with the interact key (E) could open the sockets of an item instead, and leave the mouse locked
    // and hidden until the inventory was closed with the keyboard.
    // The game's hold-to-stack: when a chest is opened with E held, InventoryGui.Show sets m_waitForContainerStack, and
    // while it's set the game keeps the mouse captured, locked at the center of the screen and hidden (GameCamera.
    // UpdateMouseCapture), so that holding E stacks your items into the chest. The chest's own update (InventoryGui.
    // UpdateContainer) clears it when E is released.
    // Jewelcrafting ("Interact Behaviour" Hovering, with "Inventory Socketing" on) opens the sockets of the item under
    // the mouse when E is held with the inventory open, in a prefix on UpdateContainer. Right after a chest opens, E is
    // still held and the mouse is at the center of the screen, where AzuEPI's equipment slots can be: it closes the chest,
    // opens that item's sockets, and from then on skips the game's UpdateContainer, so the flag is never cleared.
    // So: Jewelcrafting's check doesn't run while the E press that opened a chest is held (the flag is set), and the flag
    // is cleared whenever the inventory is open with no chest, there being nothing to stack into. Once the inventory is
    // closed the flag doesn't matter (the mouse is captured anyway) and the game clears it the next time it opens, so it's
    // left alone then: holding E on a chest stacks your items into it and closes the inventory with the flag set.
    internal static class ChestUseKey
    {
        private const string JewelcraftingGuid = "org.bepinex.plugins.jewelcrafting";

        [HarmonyPatch]
        private static class JewelcraftingSockets
        {
            private static MethodBase _target;

            // Jewelcrafting loads first when it's installed (a soft dependency of the plugin)
            private static bool Prepare()
            {
                if (_target != null)
                {
                    return true;
                }
                if (!Chainloader.PluginInfos.ContainsKey(JewelcraftingGuid))
                {
                    return false;
                }

                _target = AccessTools.Method("Jewelcrafting.GemStones+OpenFakeSocketsContainer:Prefix");
                if (_target == null)
                {
                    Plugin.Log.LogWarning("Jewelcrafting's socket window wasn't found (a new version?), so opening a chest may still open an item's sockets.");
                }
                return _target != null;
            }

            private static MethodBase TargetMethod()
            {
                return _target;
            }

            // Jewelcrafting's prefix returns whether the game's UpdateContainer runs: it does, for the chest
            private static bool Prefix(ref bool __result)
            {
                InventoryGui gui = InventoryGui.instance;
                if (!Plugin.FixChestSockets || gui == null || !gui.m_waitForContainerStack)
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        private static class ReleaseMouse
        {
            private static void Postfix(InventoryGui __instance)
            {
                // The animator's flag: Hide clears it at once, InventoryGui.IsVisible() only a frame later
                if (Plugin.FixChestSockets && __instance.m_waitForContainerStack && __instance.m_currentContainer == null &&
                    __instance.m_animator.GetBool("visible"))
                {
                    __instance.m_waitForContainerStack = false;
                    Plugin.Log.LogInfo("A chest closed while the interact key was held: released the mouse.");
                }
            }
        }
    }
}
