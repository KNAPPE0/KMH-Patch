using System;
using System.Collections.Generic;
using GameClient.Dialogs;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Patches
{
    // Redirect RWT's stock DLG_GuildList to KMH's Dialog_KMHGuildHall when connected to a KMH-enabled server
    //
    // Stock RWT's guild list is a flat "Username - Rank" scroll. KMH's Guild Hall adds Manage (promote/demote/kick),
    // MOTD edit, perks with Buy buttons, diplomacy (propose/accept/break/declare/clear), and a Settings sub-dialog
    //
    // Gating: only redirect on a KMH server. On stock-RWT servers the GuildCache stays empty so opening our dialog
    // would render "Loading..." indefinitely
    //
    // closing a window inside its own ctor corrupts the WindowStack, swap waits a frame
    [HarmonyPatch(typeof(DLG_GuildList))]
    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(List<GuildMember>) })]
    internal static class Patch_DLG_GuildList_KmhRedirect
    {
        [HarmonyPostfix]
        private static void Postfix(DLG_GuildList __instance)
        {
            if (!KmhDispatcher.IsKmhServer) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    __instance.Close(doCloseSound: false);
                    Find.WindowStack.Add(new Dialog_KMHGuildHall());
                    KmhLog.Info("Redirected RWT DLG_GuildList → Dialog_KMHGuildHall");
                }
                catch (Exception ex)
                {
                    KmhLog.Warn($"GuildList redirect threw: {ex.Message}");
                }
            });
        }
    }
}
