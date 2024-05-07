using System;
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
using UnityEngine.Rendering;
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
        [HarmonyPatch(typeof(Volume), nameof(Volume.Update))]
        [HarmonyPostfix]
        public static void PatchPostProcessing(ref Volume __instance)
        {
            if (__instance == null) return;
            var volumeProfile = __instance.profile;
            if (volumeProfile == null) return;
            volumeProfile.TryGet(out DepthOfField dof);
            if (dof) {
                dof.active = _bDepthOfField.Value switch {
                    true  => true,
                    false => false
                };
            }
            
            volumeProfile.TryGet(out Bloom bloom);
            if (bloom) {
                bloom.active = _bBloom.Value switch {
                    true  => true,
                    false => false
                };
            }
            
            volumeProfile.TryGet(out ChromaticAberration ca);
            if (ca) {
                ca.active = _bChromaticAberration.Value switch {
                    true  => ca.active,
                    false => false
                };
            }
            
            volumeProfile.TryGet(out LensDistortion ld);
            if (ld) {
                ld.active = _bLensDistortion.Value switch {
                    true  => ld.active,
                    false => false
                };
            }
            
            volumeProfile.TryGet(out Vignette vg);
            if (vg) {
                vg.active = _bVignette.Value switch {
                    true  => vg.active,
                    false => false
                };
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
    }
}