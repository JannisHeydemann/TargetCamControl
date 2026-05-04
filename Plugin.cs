using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Rewired;
using Rewired.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TargetCamControl
{
    [BepInPlugin("com.noms.targetcamcontrol", "Tactical Camera Controller", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static new ManualLogSource Log;

        // Action names — registered with InputFramework, bound by user in vanilla controls UI.
        public const string ACT_PAN_AXIS  = "TargetCamControl::PanAxis";
        public const string ACT_TILT_AXIS = "TargetCamControl::TiltAxis";
        public const string ACT_ZOOM_AXIS = "TargetCamControl::ZoomAxis";
        public const string ACT_PAN_LEFT   = "TargetCamControl::PanLeft";
        public const string ACT_PAN_RIGHT  = "TargetCamControl::PanRight";
        public const string ACT_TILT_UP    = "TargetCamControl::TiltUp";
        public const string ACT_TILT_DOWN  = "TargetCamControl::TiltDown";
        public const string ACT_ZOOM_IN    = "TargetCamControl::ZoomIn";
        public const string ACT_ZOOM_OUT   = "TargetCamControl::ZoomOut";
        public const string ACT_RESET      = "TargetCamControl::ResetView";
        public const string ACT_TOGGLE_MAN = "TargetCamControl::ToggleManual";
        public const string ACT_FORCE_COL  = "TargetCamControl::ForceColor";

        // Tunables (config-bound)
        internal static ConfigEntry<float> PanSpeed;       // deg/sec for button input
        internal static ConfigEntry<float> TiltSpeed;
        internal static ConfigEntry<float> ZoomSpeed;      // deg/sec FOV change
        internal static ConfigEntry<float> AxisSensitivity; // multiplier for analog axis
        internal static ConfigEntry<float> MinFOV;
        internal static ConfigEntry<float> MaxFOV;
        internal static ConfigEntry<float> AutoLockMinDist; // skip closer hits (self / grazing)
        internal static ConfigEntry<float> AutoLockMaxDist; // max raycast distance
        internal static ConfigEntry<bool>  ManualOnByDefault;

        // Runtime state
        internal static bool ManualMode = false;
        internal static float DesiredFOV = float.NaN;
        // World-space camera direction we're steering. Initialized from aircraft.forward
        // when manual mode turns on. Updated by pan/tilt deltas in world axes (yaw around
        // world up, pitch around current right) so the view doesn't tumble with aircraft
        // bank/yaw and the user can pan sideways even while the aircraft is maneuvering.
        internal static Vector3 PanDir = Vector3.forward;
        internal static bool HasPanDir = false;
        internal static bool HasLastHit = false;
        // Stored as GlobalPosition (Datum-relative absolute coords) so the lock survives
        // Nuclear Option's floating-origin rebasing.
        internal static GlobalPosition LastHitGP;

        // Reflection cache for TargetCam private fields
        internal static FieldInfo F_TargetCam_cam;
        internal static FieldInfo F_TargetCam_targetFOV;
        internal static FieldInfo F_TargetCam_IRMode;
        internal static FieldInfo F_TargetCam_camTimeout;
        internal static FieldInfo F_TargetCam_currentMount;
        internal static FieldInfo F_TargetCam_camMountForward;
        internal static FieldInfo F_TargetCam_canvasObjectTarget;
        internal static FieldInfo F_TargetCam_currentMode;
        internal static FieldInfo F_TargetCam_canvasObjectLanding;
        internal static MethodInfo M_TargetCam_SetTargetCam;

        void Awake()
        {
            Instance = this;
            Log = Logger;

            // Config
            PanSpeed = Config.Bind("Tuning", "PanSpeedDegPerSec",  60f, "Pan rate when button input is held");
            TiltSpeed = Config.Bind("Tuning", "TiltSpeedDegPerSec", 60f, "Tilt rate when button input is held");
            ZoomSpeed = Config.Bind("Tuning", "ZoomSpeedDegPerSec", 30f, "FOV change rate (smaller FOV = more zoom)");
            AxisSensitivity = Config.Bind("Tuning", "AxisSensitivity", 1f, "Multiplier for analog Pan/Tilt/Zoom axes");
            MinFOV = Config.Bind("Tuning", "MinFOV", 0.25f, "Tightest zoom (smallest FOV in degrees) — 0.25 = 40x magnification");
            MaxFOV = Config.Bind("Tuning", "MaxFOV", 80f,   "Widest view (largest FOV in degrees)");
            AutoLockMinDist = Config.Bind("Tuning", "AutoLockMinDist", 50f,    "Ignore raycast hits closer than this (meters) — filters self / grazing");
            AutoLockMaxDist = Config.Bind("Tuning", "AutoLockMaxDist", 100000f, "Maximum raycast distance (meters)");
            ManualOnByDefault = Config.Bind("Defaults", "ManualOnAtStart", false, "Start with manual pan/tilt enabled");
            ManualMode = ManualOnByDefault.Value;

            // We register actions directly into Rewired ourselves (in Patch_RewiredAwake
            // below) so they end up in the Camera category, where joystick/HOTAS bindings
            // are accepted. ExtraInputFramework's Debug-only registration won't work for HOTAS.

            // Reflection cache
            try
            {
                var tcType = typeof(TargetCam);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                F_TargetCam_cam = tcType.GetField("cam", flags);
                F_TargetCam_targetFOV = tcType.GetField("targetFOV", flags);
                F_TargetCam_IRMode = tcType.GetField("IRMode", flags);
                F_TargetCam_camTimeout = tcType.GetField("camTimeout", flags);
                F_TargetCam_currentMount = tcType.GetField("currentMount", flags);
                F_TargetCam_camMountForward = tcType.GetField("camMountForward", flags);
                F_TargetCam_canvasObjectTarget = tcType.GetField("canvasObjectTarget", flags);
                F_TargetCam_currentMode = tcType.GetField("currentMode", flags);
                F_TargetCam_canvasObjectLanding = tcType.GetField("canvasObjectLanding", flags);
                M_TargetCam_SetTargetCam = tcType.GetMethod("SetTargetCam",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (F_TargetCam_cam == null || F_TargetCam_targetFOV == null || F_TargetCam_IRMode == null
                    || F_TargetCam_camTimeout == null || M_TargetCam_SetTargetCam == null)
                    Log.LogWarning("TargetCam reflection: one or more members not found — check game version");
            }
            catch (Exception e) { Log.LogError($"Reflection setup: {e.Message}"); }

            // Harmony patching
            new Harmony("com.noms.targetcamcontrol").PatchAll();

            // Spawn the runner that ticks input each frame
            SceneManager.sceneLoaded += (s, m) =>
            {
                if (Runner.Instance == null)
                {
                    var go = new GameObject("TargetCamControlRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<Runner>();
                }
            };

            Log.LogInfo($"Tactical Camera Controller v1.0.0 loaded. Bind keys in Settings → Controls → Debug category.");
        }
    }

    // ====================================================================
    // Harmony: register our actions directly into Rewired's UserData under the
    // "Debug" category (the only category NO's controls UI exposes for custom
    // actions). This replaces ExtraInputFramework's role — we own registration
    // ourselves so the plugin has no external Rewired dependency.
    // ====================================================================
    [HarmonyPatch(typeof(InputManager_Base), "Awake")]
    static class Patch_RewiredAwake
    {
        static readonly (string name, InputActionType type)[] ACTIONS = {
            (Plugin.ACT_PAN_AXIS,    InputActionType.Axis),
            (Plugin.ACT_TILT_AXIS,   InputActionType.Axis),
            (Plugin.ACT_ZOOM_AXIS,   InputActionType.Axis),
            (Plugin.ACT_PAN_LEFT,    InputActionType.Button),
            (Plugin.ACT_PAN_RIGHT,   InputActionType.Button),
            (Plugin.ACT_TILT_UP,     InputActionType.Button),
            (Plugin.ACT_TILT_DOWN,   InputActionType.Button),
            (Plugin.ACT_ZOOM_IN,     InputActionType.Button),
            (Plugin.ACT_ZOOM_OUT,    InputActionType.Button),
            (Plugin.ACT_RESET,       InputActionType.Button),
            (Plugin.ACT_TOGGLE_MAN,  InputActionType.Button),
            (Plugin.ACT_FORCE_COL,   InputActionType.Button),
        };

        [HarmonyPrefix]
        static void Prefix(InputManager_Base __instance)
        {
            try
            {
                var userData = __instance._userData;
                if (userData == null) return;

                var cats = userData.actionCategories;
                var actions = userData.actions;
                if (cats == null || actions == null) return;

                InputCategory targetCat = null;
                foreach (var c in cats) if (c.name == "Debug") { targetCat = c; break; }
                if (targetCat == null)
                {
                    Plugin.Log.LogWarning("[TCC] Debug category not found — actions will not be registered");
                    return;
                }

                int nextId = 1000;
                foreach (var a in actions) if (a.id >= nextId) nextId = a.id + 1;

                int added = 0, skipped = 0;
                foreach (var (name, type) in ACTIONS)
                {
                    bool exists = false;
                    foreach (var a in actions) if (a.name == name) { exists = true; break; }
                    if (exists) { skipped++; continue; }

                    var action = new InputAction
                    {
                        id = nextId++,
                        name = name,
                        type = type,
                        descriptiveName = name,
                        categoryId = targetCat.id,
                    };
                    action._userAssignable = true;

                    actions.Add(action);
                    userData.actionCategoryMap.AddAction(targetCat.id, action.id);
                    added++;
                }
                Plugin.Log.LogInfo($"[TCC] Registered {added} actions in Debug category (skipped {skipped} existing)");
            }
            catch (Exception e) { Plugin.Log.LogError($"[TCC] Action registration: {e}"); }
        }
    }

    // ====================================================================
    // Harmony: when manual lock is active and there's no real target, populate
    // the MFD's info panel (RNG / ALT / HDG / GRID / MAG / MODE / etc.) with
    // values computed from our locked hit point.
    // ====================================================================
    [HarmonyPatch(typeof(TargetScreenUI), "UpdateTargetInfo")]
    static class Patch_TargetScreenUI_UpdateInfo
    {
        [HarmonyPrefix]
        static bool Prefix(TargetScreenUI __instance)
        {
            if (!Plugin.ManualMode || !Plugin.HasLastHit) return true; // let vanilla run
            try
            {
                var t = Traverse.Create(__instance);
                var targetList = t.Field("targetList").GetValue<List<Unit>>();
                if (targetList != null && targetList.Count > 0) return true; // real target exists

                var hud = SceneSingleton<CombatHUD>.i;
                if (hud == null || hud.aircraft == null) return true;
                var aircraft = hud.aircraft;
                var targetCam = aircraft.targetCam;
                if (targetCam == null) return true;

                // Toggle info display ON (vanilla turns it off when targetList empty)
                var displayingInfoF = t.Field("displayingInfo");
                bool displaying = displayingInfoF.GetValue<bool>();
                if (!displaying)
                {
                    var toggle = typeof(TargetScreenUI).GetMethod("ToggleInfoDisplay",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    toggle?.Invoke(__instance, new object[] { true });
                }

                // Compute lock-relative info
                Vector3 hitLocal = Plugin.LastHitGP.ToLocalPosition();
                GlobalPosition hitGP = Plugin.LastHitGP;
                GlobalPosition acGP = aircraft.GlobalPosition();
                Vector3 delta = hitGP - acGP;
                float dist = delta.magnitude;
                float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                if (bearing < 0f) bearing += 360f;
                float closure = -Vector3.Dot(aircraft.rb.velocity, delta.normalized);

                // Populate text fields
                var magText = t.Field("magText").GetValue<Text>();
                var distance = t.Field("distance").GetValue<Text>();
                var modeText = t.Field("modeText").GetValue<Text>();
                var gridText = t.Field("gridText").GetValue<Text>();
                var typeText = t.Field("typeText").GetValue<Text>();
                var heading = t.Field("heading").GetValue<Text>();
                var altitude = t.Field("altitude").GetValue<Text>();
                var rel_altitude = t.Field("rel_altitude").GetValue<Text>();
                var speed = t.Field("speed").GetValue<Text>();
                var rel_speed = t.Field("rel_speed").GetValue<Text>();
                var bearingText = t.Field("bearingText").GetValue<Text>();
                var bearingImg = t.Field("bearingImg").GetValue<Image>();
                var noLock = t.Field("noLock").GetValue<Text>();

                if (magText)     magText.text     = $"Mag x{targetCam.GetMag():F1}";
                if (distance)    distance.text    = "RNG " + UnitConverter.DistanceReading(dist);
                if (modeText)    modeText.text    = targetCam.UsingIR() ? "MODE: IR" : "MODE: COLOR";
                if (gridText)    gridText.text    = "GRID: " + targetCam.GetGrid();
                if (typeText)  { typeText.text   = "MANUAL LOCK"; typeText.color = Color.cyan; }
                if (heading)     heading.text     = $"HDG {bearing:F0}°";
                if (altitude)    altitude.text    = "ALT " + UnitConverter.AltitudeReading(hitGP.y);
                if (rel_altitude) rel_altitude.text = "REL " + UnitConverter.AltitudeReading(delta.y);
                if (speed)       speed.text       = "SPD 0 km/h";
                if (rel_speed)   rel_speed.text   = "REL " + UnitConverter.SpeedReading(closure);

                if (bearingText && bearingImg)
                {
                    var camMount = targetCam.GetCamMount();
                    if (camMount != null)
                    {
                        bearingText.text = $"{camMount.transform.localEulerAngles.y:F0}°";
                        bearingImg.rectTransform.localEulerAngles =
                            new Vector3(0f, 0f, -camMount.transform.localEulerAngles.y);
                    }
                }

                if (noLock != null && noLock.gameObject.activeSelf)
                    noLock.gameObject.SetActive(false);

                return false; // skip vanilla
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[TCC] UpdateInfo prefix: {e.Message}");
                return true;
            }
        }
    }

    // ====================================================================
    // Harmony: while in manual mode, suppress game-initiated IR state changes
    // so the user's manual toggle persists. Game's SetTargetCam re-evaluates
    // IR every call (time of day / distance) — we block that in manual mode.
    // ====================================================================
    [HarmonyPatch(typeof(TargetCam), "SwitchIRState")]
    static class Patch_TargetCam_SwitchIRState
    {
        // Set true by Runner just before invoking SwitchIRState ourselves so the
        // prefix knows to allow it through.
        public static bool AllowNext = false;

        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!Plugin.ManualMode) return true;
            if (AllowNext) { AllowNext = false; return true; }
            return false; // block external IR changes while in manual mode
        }
    }

    // ====================================================================
    // Harmony: in manual mode, skip game's per-frame target lock-on so the
    // user's own pan/tilt isn't immediately overwritten by AimCamera's
    // LookRotation toward the target.
    // ====================================================================
    [HarmonyPatch(typeof(TargetCam), "AimCamera")]
    static class Patch_TargetCam_AimCamera
    {
        [HarmonyPrefix]
        static bool Prefix() => !Plugin.ManualMode;
    }

    // ====================================================================
    // Harmony: in manual mode, skip the entire Update body except exposure.
    // Game's Update would otherwise:
    //  - Lerp cam.fieldOfView toward targetFOV (we control FOV ourselves)
    //  - Switch currentMount (forward<->rear) based on direction to target,
    //    which zeroes cam.transform.localEulerAngles every frame and kills
    //    our pan/tilt offset. With no target locked, targetPosition is (0,0,0)
    //    so the angle calc is unstable and mount-switching can fire constantly.
    //  - Decrement camTimeout (we pin it high anyway)
    // ====================================================================
    [HarmonyPatch(typeof(TargetCam), "Update")]
    static class Patch_TargetCam_Update
    {
        [HarmonyPrefix]
        static bool Prefix(TargetCam __instance)
        {
            if (!Plugin.ManualMode) return true;

            // Preserve exposure update (cosmetic IR contrast adjustment) so
            // toggling between IR/color still looks correct.
            try
            {
                var lastExpField = typeof(TargetCam).GetField("lastExposureUpdate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (lastExpField != null)
                {
                    float last = (float)lastExpField.GetValue(__instance);
                    if (Time.timeSinceLevelLoad - last > 1f)
                    {
                        var m = typeof(TargetCam).GetMethod("UpdateExposure",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        m?.Invoke(__instance, null);
                        lastExpField.SetValue(__instance, Time.timeSinceLevelLoad);
                    }
                }
            }
            catch { /* exposure failure is non-fatal */ }

            return false; // skip the rest of vanilla Update
        }
    }
}
