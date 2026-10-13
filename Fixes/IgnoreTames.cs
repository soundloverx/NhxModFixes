using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Tamed creatures following the player get between them and what they aim at: a flying one (RtD's fairies) hovers
    // over their head, where the camera looks, and the interact key pets it or commands it instead of opening the door
    // or chest behind it.
    // The game aims at the first thing the camera's ray hits (Player.FindHoverObject, every frame): that's what the
    // interact key and items used from the hotbar act on, what the hover text describes, and the creature whose name and
    // health are shown on hover (BetterTames' protection key acts on that one).
    // A key, each player's own, turns this on and off: while on, when the game's pick is a tame, the search is done again
    // the same way, going through tames. Only the local player's aim changes: tames still follow, fight and can be hit.
    [HarmonyPatch]
    internal static class IgnoreTames
    {
        // The game's ray length. CLLC replaces it with its nameplate range, at least 50 m (50 in HeavyMods)
        private const float HoverRange = 50f;

        // Modifiers that must not be held unless the shortcut names them, so that the key doesn't also fire on another
        // mod's Ctrl or Alt shortcut. Shift isn't one of them: it's the run key
        private static readonly KeyCode[] StrictModifierKeys =
        {
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftCommand, KeyCode.RightCommand,
            KeyCode.LeftWindows, KeyCode.RightWindows,
            KeyCode.AltGr
        };

        // As large as the game's
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        // Off whenever the game starts
        private static bool _ignoring;

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        [HarmonyPostfix]
        private static void CheckKey(Player __instance)
        {
            if (__instance != Player.m_localPlayer || !__instance.TakeInput() || !IsDown(Plugin.IgnoreTamesKey))
            {
                return;
            }

            _ignoring = !_ignoring;
            string text = _ignoring ? "Tame interactions off" : "Tame interactions on";
            __instance.Message(MessageHud.MessageType.Center, text);
            Plugin.Log.LogInfo($"{text}.");
        }

        // After BetterTames' postfix, which picks a tame lying down to recover when nothing else was
        [HarmonyPatch(typeof(Player), nameof(Player.FindHoverObject))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void SkipTames(Player __instance, ref GameObject hover, ref Character hoverCreature)
        {
            if (!_ignoring || __instance != Player.m_localPlayer)
            {
                return;
            }
            if (!IsTame(hoverCreature) && (hover == null || !IsTame(hover.GetComponentInParent<Character>())))
            {
                return;
            }

            FindHoverObject(__instance, out hover, out hoverCreature);
        }

        // The game's search, skipping what belongs to a tame: the first hit that isn't the player is the creature hovered
        // (unless it sleeps or the mist hides it), and what's interacted with if it's within reach
        private static void FindHoverObject(Player player, out GameObject hover, out Character hoverCreature)
        {
            hover = null;
            hoverCreature = null;

            Transform camera = GameCamera.instance.transform;
            int count = Physics.RaycastNonAlloc(camera.position, camera.forward, Hits, HoverRange, player.m_interactMask);
            Array.Sort(Hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                Rigidbody body = hit.collider.attachedRigidbody;
                if (body != null && body.gameObject == player.gameObject)
                {
                    continue;
                }
                if (IsTame(hit.collider.GetComponentInParent<Character>()))
                {
                    continue;
                }

                Character character = body != null ? body.GetComponent<Character>() : hit.collider.GetComponent<Character>();
                BaseAI ai = character != null ? character.GetBaseAI() : null;
                if (character != null && (ai == null || !ai.IsSleeping()) && !ParticleMist.IsMistBlocked(player.GetCenterPoint(), character.GetCenterPoint()))
                {
                    hoverCreature = character;
                }

                Hoverable hoverable = hit.collider.GetComponentInParent<Hoverable>();
                float reach = player.m_maxInteractDistance + (hoverable != null ? hoverable.GetHoverOffset() : 0f);
                if (Vector3.Distance(player.m_eye.position, hit.point) < reach)
                {
                    if (hit.collider.GetComponent<Hoverable>() != null)
                    {
                        hover = hit.collider.gameObject;
                    }
                    else if (body != null)
                    {
                        hover = body.gameObject;
                    }
                    else
                    {
                        hover = hit.collider.gameObject;
                    }
                }
                break;
            }
        }

        private static bool IsTame(Character character)
        {
            return character != null && character.IsTamed();
        }

        // BepInEx's KeyboardShortcut.IsDown() fails whenever a key outside the shortcut is held, so it wouldn't work while
        // walking. This ignores other keys except the strict modifiers (as NhxHotbarSwap does)
        private static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None
                || !ZInput.GetKeyDown(shortcut.MainKey, false)
                || !shortcut.Modifiers.All(key => ZInput.GetKey(key, false)))
            {
                return false;
            }

            return !StrictModifierKeys.Any(key => key != shortcut.MainKey
                && !shortcut.Modifiers.Contains(key)
                && ZInput.GetKey(key, false));
        }
    }
}
