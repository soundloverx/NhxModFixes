using HarmonyLib;
using System;
using System.Collections.Generic;

namespace NhxModFixes.Fixes
{
    // The build menu's piece buttons (BuildUiPieceButton) are pooled and set up again each time BuildUi.
    // UpdatePieceButtons shows them: when the menu opens, a category is picked, a search is typed, or the number of
    // pieces the player can build changes. Each Setup adds the button's RefreshSearchTerm to the static
    // Localization.OnLanguageChange, and nothing ever removes it. Delegate.Combine copies the whole list each time, so
    // every refresh costs more than the last as the session goes on, and destroyed buttons (the repair button is
    // destroyed and made again on each refresh) stay in memory.
    // So the button's old entry is removed before Setup adds it again, the repair button's before it's destroyed, and
    // every button's when the menu itself is destroyed (logout).
    [HarmonyPatch]
    internal static class BuildMenuSubscriptions
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.Setup))]
        private static void BeforeSetup(BuildUiPieceButton __instance)
        {
            if (Plugin.FixBuildMenu)
            {
                Unsubscribe(__instance);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.UpdatePieceButtons))]
        private static void BeforeUpdate(BuildUi __instance)
        {
            if (Plugin.FixBuildMenu)
            {
                Unsubscribe(__instance.m_specialPieceButton);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OnDestroy))]
        private static void BeforeDestroy(BuildUi __instance)
        {
            if (Plugin.FixBuildMenu)
            {
                foreach (BuildUiPieceButton button in __instance.GetComponentsInChildren<BuildUiPieceButton>(true))
                {
                    Unsubscribe(button);
                }
            }
        }

        // RemoveAll also clears the copies left from while the setting was off. A delegate made again from the same
        // button and method equals the one Setup added, destroyed button or not, so this is a reference check.
        private static void Unsubscribe(BuildUiPieceButton button)
        {
            if (!ReferenceEquals(button, null) && Localization.OnLanguageChange != null)
            {
                Localization.OnLanguageChange = (Action)Delegate.RemoveAll(Localization.OnLanguageChange, new Action(button.RefreshSearchTerm));
            }
        }

        // For the session report: only the build buttons' entries (VNEI adds one per item it lists, ~5600 in HeavyMods,
        // once per game). Fixed, there's one per button and none for deleted ones; otherwise they pile up.
        internal static string Report()
        {
            Delegate[] handlers = Localization.OnLanguageChange?.GetInvocationList() ?? new Delegate[0];
            HashSet<BuildUiPieceButton> buttons = new HashSet<BuildUiPieceButton>();
            int entries = 0;
            int deleted = 0;
            foreach (Delegate handler in handlers)
            {
                if (handler.Target is BuildUiPieceButton button)
                {
                    entries++;
                    if (button == null)
                    {
                        deleted++;
                    }
                    buttons.Add(button);
                }
            }
            return $"build menu: {entries} language-change handlers for {buttons.Count} buttons ({deleted} for deleted ones)";
        }
    }
}
