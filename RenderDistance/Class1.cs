using UnityEngine;
using BepInEx;
using HarmonyLib;
using System;
using System.Security;
using System.Security.Permissions;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace DSP_RenderDistance
{
    [BepInPlugin("com.sp00ktober.DSPMods", "RenderDistance", "0.6.0")]
    public class DSP_RenderDistance : BaseUnityPlugin
    {

		internal static GameObject dofblur, dofblurSlider, farRender, farRenderSlider;
		internal static PlanetData remoteViewPlanet;
		internal static int origPlanetId = -1;

		internal static Vector3 origPosVec3 = Vector3.zero;
		internal static VectorLF3 origPos = VectorLF3.zero;

        public void Awake()
        {

            Harmony.CreateAndPatchAll(typeof(DSP_RenderDistance));

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
			GameMain.data.mainPlayer.transform.localScale = Vector3.one;
			GameMain.data.hidePlayerModel = hidePlayer;

			GameMain.data.ArrivePlanet(dest);
			GameMain.universeSimulator.GameTick(0.0);
			GameCamera.instance.FrameLogic();

		}

		private static int findPlanet(int id, StarData star)
        {

			for(int i = 0; i < star.planetCount; i++)
            {
				if(star.planets[i].id == id)
                {
					return i;
                }
            }

			return -1;

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

		}

		[HarmonyPostfix, HarmonyPatch(typeof(UIStarmap), "OnPlanetClick")]
		public static void patch_OnPlanetClick(UIStarmap __instance, UIStarmapPlanet planet)
        {

			UIGame ui = UIRoot.instance.uiGame;
			if (ui != null && planet.planet != null)
            {

				remoteViewPlanet = planet.planet;
				origPlanetId = GameMain.data.localPlanet.id;

				origPos = GameMain.data.mainPlayer.uPosition;
				origPosVec3 = GameMain.data.mainPlayer.position;

				tpPlayer(planet.planet, true);

				__instance._OnClose();
				ui.globemap.FadeIn();

            }
			
        }

		[HarmonyPostfix, HarmonyPatch(typeof(UIGlobemap), "_OnClose")]
		public static void patch__OnClose()
        {

			if (origPlanetId != -1)
			{

				Player mainPlayer = Traverse.Create(GameMain.data).Property("mainPlayer").GetValue<Player>();

				if (mainPlayer != null)
				{

					StarData localStar = Traverse.Create(GameMain.data).Property("localStar").GetValue<StarData>();

					if (localStar != null)
					{

						for (int i = 0; i < localStar.planetCount; i++)
						{
							if (localStar.planets[i].id == origPlanetId)
							{

								tpPlayer(localStar.planets[i], false);

								break;
							}

						}

					}

				}

				origPlanetId = -1;
				remoteViewPlanet = null;

				origPos = VectorLF3.zero;
				origPosVec3 = Vector3.zero;

			}

		}

		[HarmonyPostfix, HarmonyPatch(typeof(UIGlobemap), "FadeIn")]
		public static void patch_FadeIn()
        {

			Traverse.Create(GameMain.data).Property("localPlanet").GetValue<PlanetData>().LoadFactory();

        }

		[HarmonyPrefix, HarmonyPatch(typeof(GameData), "LeavePlanet")]
		public static bool patch_LeavePlanet(GameData __instance)
        {

			if (__instance.localPlanet != null)
			{
				__instance.localPlanet.UnloadFactory();
				__instance.localPlanet.onLoaded -= __instance.OnActivePlanetLoaded;
				__instance.localPlanet.onFactoryLoaded -= __instance.OnActivePlanetFactoryLoaded;
				if(origPlanetId == -1)
                {
					__instance.localPlanet = null;
				}
			}
			if(origPlanetId == -1)
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
				slider.value = 10.0f;
				//slider.stepSize = 0.1f; // does not work as its write protected

				farRenderSlider.GetComponentInChildren<Text>().text = ((float)slider.value / 10f).ToString("0.0");
				farRender.GetComponent<Text>().text = "Planet Render Distance Multiplier";

				Debug.Log("injected UI into settings");

            }
			else if(farRender != null && farRenderSlider != null)
            {

				UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();
				farRenderSlider.GetComponentInChildren<Text>().text = ((float)slider.value / 10f).ToString("0.0");
				farRender.GetComponent<Text>().text = "Planet Render Distance Multiplier";

				Debug.Log("re-injected UI into settings");

			}
            else
            {
				Debug.Log("could not inject UI into settings");
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
				double num5 = num2; // this is vanilla

				try
				{
					UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();
					if (slider != null && slider.gameObject != null) // im checking the wrong thing here i guess
					{
						num5 = num2 * ((float)slider.value / 10f);
					}
				}
				catch (NullReferenceException e)
				{
					// this should only happen in main menu
				}

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