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
    [BepInPlugin("com.sp00ktober.DSPMods", "RenderDistance", "0.5.0")]
    public class DSP_RenderDistance : BaseUnityPlugin
    {

		internal static GameObject dofblur, dofblurSlider, farRender, farRenderSlider;

        public void Awake()
        {

            Harmony.CreateAndPatchAll(typeof(DSP_RenderDistance));

        }

		[HarmonyPostfix, HarmonyPatch(typeof(UIOptionWindow), "_OnUpdate")]
		public static void patch__OnUpdate(UIOptionWindow __instance)
        {

			if(farRenderSlider != null)
            {
				
				UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();

				if(slider.value == 0)
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
				farRender.transform.position = new Vector3(-8.43f, 49.7f, 0f);
				farRender.transform.localPosition = new Vector3(-850f, 10.0f, 0f);

				farRenderSlider = Instantiate(dofblurSlider, dofblurSlider.transform.position, Quaternion.identity);
				farRenderSlider.name = "Slider";
				farRenderSlider.transform.SetParent(farRender.transform);
				farRenderSlider.transform.localScale = new Vector3(1f, 1f, 1f);
				farRenderSlider.transform.position = new Vector3(-5.95f, 50f, 0f);
				farRenderSlider.transform.localPosition = new Vector3(250f, -23.0f, 0f);

				UnityEngine.UI.Slider slider = farRenderSlider.GetComponent<Slider>();
				slider.maxValue = 4f * 10; // can only set whole numbers, so devide by 10 to get float value back
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
				while (nearestStar != null && num6 < nearestStar.planetCount)
				{
					double num7 = (__instance.mainPlayer.uPosition - nearestStar.planets[num6].uPosition).magnitude - (double)nearestStar.planets[num6].realRadius;
					if (num7 < num5)
					{
						nearestPlanet = nearestStar.planets[num6];
						num5 = num7;
					}
					num6++;
				}
			}

			return false;

		}

    }

}