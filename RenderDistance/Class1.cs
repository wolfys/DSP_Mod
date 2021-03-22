using UnityEngine;
using BepInEx;
using HarmonyLib;
using System;
using System.Security;
using System.Security.Permissions;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Reflection.Emit;
using System.Reflection;
using BepInEx.Configuration;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace DSP_RenderDistance
{
[BepInPlugin("com.sp00ktober.DSPMods", "RenderDistance", "0.7.0")]
public class DSP_RenderDistance : BaseUnityPlugin
{

internal static GameObject dofblur, dofblurSlider, farRender, farRenderSlider;
internal static PlanetData remoteViewPlanet;
internal static int origPlanetId = -1;
internal static int origStarId = -1;

internal static Vector3 origPosVec3 = Vector3.zero;
internal static VectorLF3 origPos = VectorLF3.zero;

internal static Quaternion origRot = new Quaternion(0, 0, 0, 0);
internal static VectorLF3 origVelocity = VectorLF3.zero;

internal static bool needUpdateRot = false;

internal static EMovementState origMoveState = EMovementState.Walk;
internal static OrderNode currentOrder = null;

internal static bool exitedGlobalView = false;
internal static PlanetData starmapSelectedPlanet = null;

private static ConfigFile customConfig {
        get; set;
}
public static ConfigEntry<float> RenderDistConfEntry {
        get; set;
}
public static ConfigEntry<int> AllowRemoteILSAccess {
        get; set;
}
public static ConfigEntry<bool> EnableRemotePlanetView {
        get; set;
}

public void Awake()
{

        Harmony.CreateAndPatchAll(typeof(DSP_RenderDistance));

        customConfig = new ConfigFile(Paths.ConfigPath + "\\RenderDistance.cfg", true);
        RenderDistConfEntry = customConfig.Bind<float>(
                "RenderDistanceMultiplier",
                "Multiplier",
                1.0f,
                "The multiplier to use for planet render distance");
        AllowRemoteILSAccess = customConfig.Bind<int>(
                "RemoteStorage",
                "Toggle",
                1,
                "If set to 0 it will allow to access any ILS/PLS on any planet. If set to 1 it will restrict that to the local solar system, if set to 2 it will restrict to the local planet (vanilla)");
        EnableRemotePlanetView = customConfig.Bind<bool>(
                "RemotePlanetView",
                "Toggle",
                true,
                "If set to true you will be able to enter planet view mode for any planet. If set to false you will have this feature disabled.");
}

private static void tpPlayer(PlanetData dest, bool hidePlayer)
{

        if (!hidePlayer)
        {
                GameMain.data.mainPlayer.uPosition = origPos;
                GameMain.data.mainPlayer.position = origPosVec3;
        }
        else
        {
                GameMain.data.mainPlayer.uPosition = dest.uPosition + VectorLF3.unit_z * (double)dest.realRadius;
        }

        if (dest != null)
        {
                GameMain.data.ArrivePlanet(dest);
        }
        else
        {
                needUpdateRot = true;
                // this is crucial because of PlayerController::LateUpdate()
                // it would reset the location back to the remote planet and leave the player there
                GameMain.data.mainPlayer.controller.actionSail.sailCounter = 5;
        }
        GameMain.data.mainPlayer.transform.localScale = Vector3.one;
        GameMain.data.hidePlayerModel = hidePlayer;
        if(origMoveState != EMovementState.Walk)
        {
                GameMain.mainPlayer.movementState = origMoveState;
        }

        GameMain.universeSimulator.GameTick(0.0);
        GameCamera.instance.FrameLogic();

}

private static int findPlanet(int id, StarData star)
{

        for (int i = 0; i < star.planetCount; i++)
        {
                if (star.planets[i].id == id)
                {
                        return i;
                }
        }

        return -1;

}

[HarmonyPrefix, HarmonyPatch(typeof(UIGame), "OpenStationWindow")]
public static bool patch_UIStationWindow_OnOpen(UIGame __instance)
{
        customConfig.Reload();
        if (AllowRemoteILSAccess.Value == 1 && origStarId != -1 && GameMain.localStar.id != origStarId)
        {
                return false;
        }
        else if(AllowRemoteILSAccess.Value == 2 && origPlanetId != -1 && GameMain.localPlanet.id != origPlanetId)
        {
                return false;
        }
        return true;
}

[HarmonyTranspiler, HarmonyPatch(typeof(PlayerOrder), "GameTick")]
public static IEnumerable<CodeInstruction> patch_PlayerOrder_GameTick(IEnumerable<CodeInstruction> instructions)
{

        instructions = new CodeMatcher(instructions)
                       .MatchForward(false,
                                     new CodeMatch(OpCodes.Ldarg_0),
                                     new CodeMatch(OpCodes.Ldfld),
                                     new CodeMatch(i => i.opcode == OpCodes.Callvirt && ((MethodInfo)i.operand).Name == "get_factory"),
                                     new CodeMatch(OpCodes.Stloc_0))
                       .Advance(3)
                       .Insert(Transpilers.EmitDelegate<Func<PlanetFactory,PlanetFactory> >(pFactory =>
                        {
                                if (origPlanetId == -1)
                                {
                                        return GameMain.mainPlayer.factory;
                                }
                                else
                                {
                                        return GameMain.galaxy.PlanetById(origPlanetId).factory;
                                }
                        })).InstructionEnumeration();

        return instructions;

}

[HarmonyPrefix, HarmonyPatch(typeof(PlayerOrder), "Enqueue")]
public static bool patch_Enqueue()
{
        if(currentOrder != null)
        {
                return false;
        }
        return true;
}

[HarmonyPrefix, HarmonyPatch(typeof(PlayerFootsteps), "PlayFootstepEffect")]
public static bool patch_PlayFootstepEffect()
{
        if(currentOrder != null)
        {
                return false;
        }
        return true;
}

[HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateRotation")]
public static bool patch_UpdateRotation(PlayerController __instance)
{

        if (needUpdateRot)
        {
                __instance.player.uRotation = origRot;
                __instance.player.uVelocity = origVelocity;
                needUpdateRot = false;

                return false;
        }

        return true;

}

[HarmonyPostfix, HarmonyPatch(typeof(PlayerController), "UpdatePhysicsDirect")]
public static void patch_UpdatePhysicsDirect(PlayerController __instance)
{

        if (origPos != VectorLF3.zero && origPosVec3 != Vector3.zero && origPlanetId != -1)
        {

                int planetIndex = findPlanet(origPlanetId, GameMain.data.localStar);
                if(planetIndex != -1)
                {

                        VectorLF3 relativePos;
                        Quaternion relativeRot;
                        PlanetData origPlanet = GameMain.data.localStar.planets[planetIndex];

                        relativePos.x = origPlanet.uPosition.x;
                        relativePos.y = origPlanet.uPosition.y;
                        relativePos.z = origPlanet.uPosition.z;
                        relativeRot.x = origPlanet.runtimeRotation.x;
                        relativeRot.y = origPlanet.runtimeRotation.y;
                        relativeRot.z = origPlanet.runtimeRotation.z;
                        relativeRot.w = origPlanet.runtimeRotation.w;

                        origPos = relativePos + Maths.QRotateLF(relativeRot, origPosVec3);

                }

        }
        else if(origPos != VectorLF3.zero && origPlanetId == -1)
        {

                origPos = origPos + Maths.QRotateLF(origRot, origPosVec3);

        }

}

[HarmonyPostfix, HarmonyPatch(typeof(UIStarmap), "OnPlanetClick")]
public static void patch_OnPlanetClick(UIStarmap __instance, UIStarmapPlanet planet)
{
        if(__instance.focusPlanet != null && EnableRemotePlanetView.Value)
        {
                starmapSelectedPlanet = planet.planet;
        }
        else
        {
                starmapSelectedPlanet = null;
        }
}

[HarmonyPostfix, HarmonyPatch(typeof(UIStarmap), "OnStarClick")]
public static void patch_OnStarClick(UIStarmap __instance, UIStarmapStar star)
{
        starmapSelectedPlanet = null;
}

[HarmonyPostfix, HarmonyPatch(typeof(UIStarmap), "OnCursorFunction2Click")]
public static void patch_OnCursorFunction2Click(UIStarmap __instance, int obj)
{

        if(GameMain.data.mainPlayer.mecha.idleDroneCount != GameMain.data.mainPlayer.mecha.droneCount)
        {
                UIMessageBox.Show("Please wait!", "Your building drones have not yet returned, please wait before you use the remote planet view feature!", "Close", 3);
                return;
        }

        UIGame ui = UIRoot.instance.uiGame;
        if (ui != null && starmapSelectedPlanet != null)
        {

                OrderNode order = GameMain.mainPlayer.orders.currentOrder;
                if (order != null && (order.type == EOrderType.Mine || (order.type == EOrderType.Move && GameMain.mainPlayer.movementState == EMovementState.Walk)))
                {
                        currentOrder = order;
                        GameMain.mainPlayer.orders.currentOrder = null;
                }

                if(origPlanetId == -1 && GameMain.data.localPlanet != null && origPos == VectorLF3.zero && GameMain.mainPlayer.movementState != EMovementState.Sail)
                {
                        origPlanetId = GameMain.data.localPlanet.id;
                        origStarId = GameMain.localStar.id;

                        origPos = GameMain.data.mainPlayer.uPosition;
                        origPosVec3 = GameMain.data.mainPlayer.position;
                }
                else if(origPos == VectorLF3.zero && GameMain.data.localPlanet == null)
                {
                        origPos = GameMain.data.mainPlayer.uPosition;
                        origPosVec3 = GameMain.data.mainPlayer.position;

                        origRot = GameMain.data.mainPlayer.uRotation;
                        origVelocity = GameMain.data.mainPlayer.uVelocity;
                }
                else if(origPos == VectorLF3.zero && GameMain.data.localPlanet != null)
                {
                        UIMessageBox.Show("Im so sorry!", "Due to a teleportation bug i prevent you from using \"remote planet view\" when sailing near a planet, sorry for the inconvenience!\n" +
                                          "\nI may will try to fix this issue in the future if i find the time to do so.\n" +
                                          "\nHope you enjoy the game C:", "Close", 3);
                        return;
                }

                remoteViewPlanet = starmapSelectedPlanet;
                origMoveState = GameMain.mainPlayer.movementState;

                exitedGlobalView = false;

                tpPlayer(starmapSelectedPlanet, true);

                __instance._OnClose();
                ui.globemap.FadeIn();

        }

}

[HarmonyPrefix, HarmonyPatch(typeof(UIGlobemap), "_OnClose")]
public static bool patch__OnClose()
{

        Player mainPlayer = GameMain.mainPlayer;

        if (mainPlayer != null)
        {

                StarData localStar = GameMain.galaxy.StarById(origStarId);

                if (localStar != null && origPlanetId != -1)
                {

                        for (int i = 0; i < localStar.planetCount; i++)
                        {
                                if (localStar.planets[i].id == origPlanetId)
                                {
                                        tpPlayer(localStar.planets[i], false);
                                        while (localStar.planets[i].loading)
                                        {
                                                PlanetModelingManager.Update();
                                        }

                                        break;
                                }

                        }

                }
                else if (origPlanetId == -1 && remoteViewPlanet != null)
                {
                        tpPlayer(null, false);
                }

                if (currentOrder != null)
                {
                        GameMain.mainPlayer.orders.currentOrder = currentOrder;
                        currentOrder = null;
                }

                remoteViewPlanet = null;

                origPos = VectorLF3.zero;
                origPosVec3 = Vector3.zero;

                exitedGlobalView = true;

                // if we remote planet view the origin planet after doing that for another planet
                // and the factory is loaded once we exit the planet view
                // we still need to reset theese but it will not get done by NotifyFactoryLoaded()
                // because we do not know there if the player actually wants to exit the planet view mode
                // or return to star map
                if(GameMain.localPlanet != null && GameMain.localPlanet.id == origPlanetId && GameMain.localPlanet.factoryLoaded)
                {
                        origPlanetId = -1;
                        origStarId = -1;

                        // so weird that returning from a planet of another system destroys theese. i dont feel food with this.
                        GameMain.mainPlayer.controller.enabled = true;
                        GameMain.mainPlayer.animator.enabled = true;
                        GameMain.mainPlayer.audio.footsteps.enabled = true;
                        GameMain.mainPlayer.audio.enabled = true;
                        GameMain.mainPlayer.effect.enabled = true;

                        GameMain.mainPlayer.controller.gameData = GameMain.data;
                        GameMain.mainPlayer.controller.player = GameMain.mainPlayer;
                        GameMain.mainPlayer.animator.player = GameMain.mainPlayer;
                        GameMain.mainPlayer.audio.player = GameMain.mainPlayer;
                        GameMain.mainPlayer.audio.footsteps.player = GameMain.mainPlayer;
                        GameMain.mainPlayer.effect.player = GameMain.mainPlayer;
                }

        }

        return true;

}

[HarmonyPostfix, HarmonyPatch(typeof(PlanetData), "NotifyFactoryLoaded")]
public static void patch_NotifyFactoryLoaded(PlanetData __instance)
{
        if(__instance.id == origPlanetId && exitedGlobalView)
        {
                origPlanetId = -1;
                origStarId = -1;

                // so weird that returning from a planet of another system destroys theese. i dont feel food with this.
                GameMain.mainPlayer.controller.enabled = true;
                GameMain.mainPlayer.animator.enabled = true;
                GameMain.mainPlayer.audio.footsteps.enabled = true;
                GameMain.mainPlayer.audio.enabled = true;
                GameMain.mainPlayer.effect.enabled = true;

                GameMain.mainPlayer.controller.gameData = GameMain.data;
                GameMain.mainPlayer.controller.player = GameMain.mainPlayer;
                GameMain.mainPlayer.animator.player = GameMain.mainPlayer;
                GameMain.mainPlayer.audio.player = GameMain.mainPlayer;
                GameMain.mainPlayer.audio.footsteps.player = GameMain.mainPlayer;
                GameMain.mainPlayer.effect.player = GameMain.mainPlayer;
        }
}

[HarmonyPrefix, HarmonyPatch(typeof(UIGlobemap), "FadeIn")]
public static bool patch_FadeIn()
{

        while (GameMain.localPlanet.loading)
        {
                PlanetModelingManager.Update();
        }
        //Traverse.Create(GameMain.data).Property("localPlanet").GetValue<PlanetData>().LoadFactory();
        return true;

}

[HarmonyPrefix, HarmonyPatch(typeof(GameData), "LeavePlanet")]
public static bool patch_LeavePlanet(GameData __instance)
{

        if (__instance.localPlanet != null)
        {
                __instance.localPlanet.UnloadFactory();
                __instance.localPlanet.onLoaded -= __instance.OnActivePlanetLoaded;
                __instance.localPlanet.onFactoryLoaded -= __instance.OnActivePlanetFactoryLoaded;
                if(origPlanetId == -1 && origPos == VectorLF3.zero)
                {
                        __instance.localPlanet = null;
                }
        }
        if(origPlanetId == -1 && origPos == VectorLF3.zero)
        {
                __instance.mainPlayer.planetId = 0;
        }

        return false;

}

[HarmonyPostfix, HarmonyPatch(typeof(UIOptionWindow), "_OnUpdate")]
public static void patch__OnUpdate(UIOptionWindow __instance)
{

        if (farRenderSlider != null)
        {

                UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();

                if (slider.value == 0)
                {
                        slider.value = 1;
                }

                farRenderSlider.GetComponentInChildren<Text>().text = ((float)slider.value / 10f).ToString("0.0");
                if(farRender != null)
                {
                        farRender.GetComponent<Text>().text = "Planet Render Distance Multiplier";
                }

                RenderDistConfEntry.Value = ((float)slider.value / 10f);

        }

}

// much thanks to https://github.com/fezhub/DSP-Mods/blob/main/DSP_SphereProgress/SphereProgress.cs
[HarmonyPostfix, HarmonyPatch(typeof(UIOptionWindow), "_OnOpen")]
public static void patch_OnOpen(UIOptionWindow __instance)
{

        dofblur = GameObject.Find("UI Root/Overlay Canvas/Top Windows/Option Window/details/content-1/dofblur");
        dofblurSlider = GameObject.Find("UI Root/Overlay Canvas/Top Windows/Option Window/details/content-1/dofblur/Slider");

        if(dofblurSlider != null && dofblur != null && farRender == null && farRenderSlider == null)
        {

                farRender = Instantiate(dofblur, dofblur.transform.position, Quaternion.identity);
                farRender.name = "farRender";
                farRender.transform.SetParent(dofblur.transform.parent);
                foreach(Transform child in farRender.transform)
                {
                        child.transform.SetParent(dofblurSlider.transform.parent);
                }
                farRender.transform.localScale = new Vector3(1f, 1f, 1f);

                Vector3 farRenderPos = farRender.transform.position;
                farRenderPos.y -= 0.4f;

                farRender.transform.position = farRenderPos;

                farRenderSlider = Instantiate(dofblurSlider, dofblurSlider.transform.position, Quaternion.identity);
                farRenderSlider.name = "Slider";
                farRenderSlider.transform.SetParent(farRender.transform);
                farRenderSlider.transform.localScale = new Vector3(1f, 1f, 1f);
                farRenderSlider.transform.position = dofblurSlider.transform.position;
                farRenderSlider.transform.localPosition = dofblurSlider.transform.localPosition;

                UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();
                slider.maxValue = 10f * 10; // can only set whole numbers, so devide by 10 to get float value back
                slider.value = RenderDistConfEntry.Value * 10;
                if(slider.value <= 0)
                {
                        slider.value = 1;
                }
                else if(slider.value > 100)
                {
                        slider.value = 100;
                }
                //slider.stepSize = 0.1f; // does not work as its write protected

                farRenderSlider.GetComponentInChildren<Text>().text = ((float)slider.value / 10f).ToString("0.0");
                farRender.GetComponent<Text>().text = "Planet Render Distance Multiplier";

        }
        else if(farRender != null && farRenderSlider != null)
        {

                UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();
                farRenderSlider.GetComponentInChildren<Text>().text = ((float)slider.value / 10f).ToString("0.0");
                farRender.GetComponent<Text>().text = "Planet Render Distance Multiplier";

                RenderDistConfEntry.Value = ((float)slider.value / 10f);

        }

}

[HarmonyPrefix, HarmonyPatch(typeof(GameData), "GetNearestStarPlanet")]
public static bool patchGetNearestStarPlanet(GameData __instance, ref StarData nearestStar, ref PlanetData nearestPlanet)
{

        if (__instance.mainPlayer == null)
        {
                nearestStar = null;
                nearestPlanet = null;
        }
        double num = 3239999.9141693115;
        double num2 = 899.9999761581421;
        if (nearestStar != null)
        {
                double magnitude = (__instance.mainPlayer.uPosition - nearestStar.uPosition).magnitude;
                num = 3600000.0;
                if (magnitude > num * 0.5)
                {
                        nearestStar = null;
                }
        }
        if (nearestPlanet != null)
        {
                double num3 = (__instance.mainPlayer.uPosition - nearestPlanet.uPosition).magnitude - (double)nearestPlanet.realRadius;
                num2 = 1000.0;
                if (num3 > num2 * 0.4000000059604645)
                {
                        nearestPlanet = null;
                }
        }
        if (nearestStar == null)
        {
                double num4 = num;
                for (int i = 0; i < __instance.galaxy.starCount; i++)
                {
                        double magnitude2 = (__instance.mainPlayer.uPosition - __instance.galaxy.stars[i].uPosition).magnitude;
                        if (magnitude2 < num4)
                        {
                                nearestStar = __instance.galaxy.stars[i];
                                num4 = magnitude2;
                        }
                }
        }
        if (__instance.mainPlayer.warping)
        {
                nearestPlanet = null;
        }
        else if (nearestPlanet == null)
        {
                double num5 = num2 * RenderDistConfEntry.Value;

                int num6 = 0;
                PlanetData fallbackGas = null;
                while (nearestStar != null && num6 < nearestStar.planetCount)
                {
                        double num7 = (__instance.mainPlayer.uPosition - nearestStar.planets[num6].uPosition).magnitude - (double)nearestStar.planets[num6].realRadius;
                        if (num7 < num5)
                        {
                                if (nearestStar.planets[num6].type == EPlanetType.Gas && num7 > 500)
                                {
                                        fallbackGas = nearestStar.planets[num6];
                                }
                                else
                                {
                                        nearestPlanet = nearestStar.planets[num6];
                                        num5 = num7;
                                }
                        }
                        num6++;
                }
                if(nearestPlanet == null && fallbackGas != null)
                {
                        nearestPlanet = fallbackGas; // to prevent ping pong loading between starter and orbiting gas planet when between them. favor home planet.
                }
        }

        if(remoteViewPlanet != null)
        {
                nearestPlanet = remoteViewPlanet;
        }

        return false;

}

}

}
