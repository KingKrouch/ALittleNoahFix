﻿using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Game;
using Game.Core;
using Game.Rendering;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;

namespace ALittleNoahFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("LittleNoah.exe")]
public partial class ALittleNoahFix : BasePlugin
{
    private static ManualLogSource? LogSource { get; set; }

    public override void Load()
    {
        LogSource = Log;

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        
        // Initializes our configuration file, alongside loading graphics options.
        InitConfig();
        LoadGraphicsSettings();
        
        // Finally, load our patches.
        Harmony.CreateAndPatchAll(typeof(MousePatches));
        Harmony.CreateAndPatchAll(typeof(ResolutionPatches));
        Harmony.CreateAndPatchAll(typeof(UIPatches));
        Harmony.CreateAndPatchAll(typeof(GraphicsPatches));
        //Harmony.CreateAndPatchAll(typeof(SteamPatches));
        //Harmony.CreateAndPatchAll(typeof(CameraPatches));
    }

    [HarmonyPatch]
    public class SteamPatches
    {
        
    }

    [HarmonyPatch]
    public class ResolutionPatches
    {
        
        [HarmonyPatch(typeof(Game.TitleScene), nameof(Game.TitleScene.Start)), HarmonyPostfix]
        //[HarmonyPatch(typeof(Game.SystemDataStatus), nameof(Game.SystemDataStatus.UpdateResolution)), HarmonyPrefix]
        public static void ForceCustomResolution()
        {
            if (_bForceCustomResolution.Value) {
                Screen.SetResolution(_iHorizontalResolution.Value, _iVerticalResolution.Value, Screen.fullScreenMode);
                Debug.Log("Resolution Value Changed to: " + Screen.currentResolution.m_Width + "x" + Screen.currentResolution.m_Height + ".");
            }
            return;
        }

        [HarmonyPatch(typeof(OptionGamePlayMenuElementResolution), nameof(OptionGamePlayMenuElementResolution.Setup)), HarmonyPostfix]
        public static void SetupResolutions(OptionGamePlayMenuElementResolution __instance)
        {
            // TODO: Override the in-game resolution options in favor of our own.
            return;
        }
    }

    [HarmonyPatch]
    public class MousePatches
    {
        // NOTE: This doesn't seem to work at the moment. If it did, that would be great, so I could see where my cursor is in UnityExplorer.
        [HarmonyPatch(typeof(WindowsPlatformService), nameof(WindowsPlatformService.SetupTransparentCursor)), HarmonyPrefix]
        public static bool NOPTransparentCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return false;
        }
    }

    [HarmonyPatch]
    public class GraphicsPatches
    {
        [HarmonyPatch(typeof(Game.BattleCamera), nameof(Game.BattleCamera.UpdateCameraParam)), HarmonyPostfix]
        public static void PatchPostProcessing(Game.BattleCamera __instance)
        {
            var volumeProfile = __instance.GetComponentInParent<UnityEngine.Rendering.Volume>().profile;
            if (volumeProfile == null) {
                Debug.LogWarning("Couldn't find a Rendering Volume.");
                return;
            }
            Debug.Log("Found Rendering Volume.");
            foreach (var component in volumeProfile.components) {
                switch (component) {
                    case DepthOfField dof:
                        Debug.Log("Disabled Depth Of Field.");
                        dof.active = _bDepthOfField.Value switch {
                            true  => true,
                            false => false
                        };
                        break;
                    case Bloom bloom:
                        Debug.Log("Disabled Bloom.");
                        bloom.active = _bBloom.Value switch {
                            true  => true,
                            false => false
                        };
                        break;
                    case ChromaticAberration ca:
                        Debug.Log("Disabled Chromatic Aberration.");
                        ca.active = _bChromaticAberration.Value switch {
                            true  => ca.active,
                            false => false
                        };
                        break;
                    case LensDistortion ld:
                        Debug.Log("Disabled Lens Distortion.");
                        ld.active = _bLensDistortion.Value switch {
                            true  => ld.active,
                            false => false
                        };
                        ld.active = false;
                        break;
                    case Vignette vg:
                        Debug.Log("Disabled Vignette.");
                        vg.active = _bVignette.Value switch {
                            true  => vg.active,
                            false => false
                        };
                        break;
                }
            }
        }
        
        [HarmonyPatch(typeof(Engine), nameof(Engine.DelayFrame)), HarmonyPrefix]
        public static bool PatchFramerateLimiter()
        {
            // Let us adjust VSync.
            QualitySettings.vSyncCount = _bvSync.Value ? 1 : 0;
            // Unlock the Framerate.
            Application.targetFrameRate = -1;
            // FixedDeltaTime is seemingly being used by most things instead of DeltaTime, which is why I assume the shitty framelimiter has been added. Anyways, after this, smooth as butter.
            Time.fixedDeltaTime = 1.0f / Screen.currentResolution.m_RefreshRate;
            return false;
        }
        // This should in theory patch the camera to use Vert+ Scaling.
        // TODO: Fix this.
        [HarmonyPatch(typeof(Game.BattleCamera), nameof(Game.BattleCamera.CalcCameraFov), MethodType.Setter), HarmonyPostfix]
        public static void PatchCameraFOV(Game.BattleCamera __instance)
        {
            var oldFOV = __instance.mCamera.fieldOfView;
            __instance.mCamera.usePhysicalProperties = true;
            __instance.mCamera.sensorSize = new Vector2(16, 9);
            __instance.mCamera.gateFit = Camera.GateFitMode.Overscan;
            __instance.mCamera.fieldOfView = oldFOV;
        }
        // Game.MiniMap -> Parent (GameObject name: Info), this is where the minimap scale can be adjusted for lower resolutions.
        // Game.Gameplay.EventDialog can have it's positioning set lower with resolutions narrower than 16:9 to prevent clipping problems.
        // or it can have it's anchor set to the lower center of the screen, so it's consistent and less math is needed, maybe...
    }

    [HarmonyPatch]
    public class UIPatches
    {
        [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
        [HarmonyPostfix]
        public static void CanvasScalerFixes(CanvasScaler __instance)
        {
            __instance.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        [HarmonyPatch(typeof(Game.BattleMenuCompornent), nameof(Game.BattleMenuCompornent.Start)), HarmonyPostfix]
        public static void AddAspectRatioFitterToGameUI(Game.BattleMenuCompornent __instance)
        {
            if (__instance.gameObject.GetComponent<AspectRatioFitter>() != null) return;
            var arf = __instance.gameObject.AddComponent<AspectRatioFitter>();
            if (arf == null) return;
            AdjustAspectRatioFitter(arf);
        }
        
        private static void AdjustAspectRatioFitter(AspectRatioFitter arf)
        {
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.enabled = true;
            // Check if the display aspect ratio is less than 16:9, and if so, disable the AspectRatioFitter and use the old transforms.
            if (Screen.currentResolution.m_Width / Screen.currentResolution.m_Height >= 1920.0f / 1080.0f) {
                arf.aspectRatio = 1920.0f / 1080.0f;
            }
            else {
                arf.aspectRatio = Screen.currentResolution.m_Width / (float)Screen.currentResolution.m_Height;
            }
        }

        //[HarmonyPatch(typeof(UIMagicBar), nameof(UIMagicBar.Init)), HarmonyPostfix]
        //public static void AddAspectRatioFitterToMagicBar(UIMagicBar __instance)
        //{
            // We need to explicitly check if it exists first, as for some reason, the component can be added twice.
            //var magicBarAspectRatioFitter = __instance.gameObject.GetComponent<AspectRatioFitter>();
            //if (magicBarAspectRatioFitter != null) return;
            //magicBarAspectRatioFitter = __instance.gameObject.AddComponent<AspectRatioFitter>();
            //Debug.Log("Adding Aspect Ratio Fitter to " + __instance.gameObject.name);
            //AdjustAspectRatioFitter(magicBarAspectRatioFitter);
        //}

        //[HarmonyPatch(typeof(UIScriptMode), "Init"), HarmonyPostfix]
        //public static void AddAspectRatioFitterToTrialTowerDialogue(UIScriptMode __instance)
        //{
        //var dialogueAspectRatioFitter = __instance.gameObject.GetComponent<AspectRatioFitter>();
        //if (dialogueAspectRatioFitter != null) return;
        //dialogueAspectRatioFitter = __instance.gameObject.AddComponent<AspectRatioFitter>();
        //Debug.Log("Adding Aspect Ratio Fitter to " + __instance.gameObject.name);
        //AdjustAspectRatioFitter(dialogueAspectRatioFitter);

            // Fix the Vignette effect with ultrawide resolutions.
            //var topVignette = __instance.transform.Find("BlackEdge/EdgeUp");
            //var bottomVignette = __instance.transform.Find("BlackEdge/EdgeBottom");
            //var fadeOut = __instance.transform.Find("BlackScreen");

            // A quick function of sorts to calculate the correct horizontal offset for fullscreen UI elements.
            //float horizontalARDifference()
            //{
            //var currentAR = (float)Screen.currentResolution.m_Width / Screen.currentResolution.m_Height;
            //var originalAR = 1920.0f / 1080.0f;
            //if (currentAR < originalAR) return 1.0f;
            //return currentAR / originalAR;
            //}

            // Create a cached variable of sorts, so we don't have to run the same function twice.
            //var offset = horizontalARDifference();
            //topVignette.transform.localScale = new Vector3(offset, 1.0f, 1.0f);
            //bottomVignette.transform.localScale = new Vector3(offset, 1.0f, 1.0f);
            //fadeOut.transform.localScale = new Vector3(offset, 1.0f, 1.0f);
            //}

            //[HarmonyPatch(typeof(StaffManager), nameof(StaffManager.PlayVideo)), HarmonyPostfix]
            //public static void FixCreditsVideoScaling(StaffManager __instance)
            //{
            //__instance.player.aspectRatio = VideoAspectRatio.FitInside;
            //}

            //[HarmonyPatch(typeof(UIVideoPlayer), nameof(UIVideoPlayer.PlayFromBeginning))]
            //public static void FixVideoPlayback(UIVideoPlayer __instance)
            //{
            //// TODO: Figure out why this code isn't working.
            //var videoPlayerComponent = __instance.gameObject.GetComponent<VideoPlayer>();
            //if (videoPlayerComponent != null) {
                // By default, the game uses FitHorizontal, which while works for ultrawide, might cause problems with resolutions narrower than 16:9.
                //videoPlayerComponent.aspectRatio = VideoAspectRatio.FitInside;
                //}
                //}

                //[HarmonyPatch(typeof(UIOpeningMenu), nameof(UIOpeningMenu.Init))]
                //public static void FixMainMenuVignette(UIOpeningMenu __instance)
                //{
            // We need to explicitly check if it exists first, as for some reason, the component can be added twice and cause slow movement.
            //var vignetteAspectRatioFitter = __instance.gameObject.GetComponent<AspectRatioFitter>();
            //if (vignetteAspectRatioFitter != null) return;
            //vignetteAspectRatioFitter = __instance.gameObject.AddComponent<AspectRatioFitter>();
            //Debug.Log("Adding Aspect Ratio Fitter to " + __instance.gameObject.name);
            //AdjustAspectRatioFitter(vignetteAspectRatioFitter);
            //}
    }
}