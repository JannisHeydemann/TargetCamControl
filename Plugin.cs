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

        // Action names (legacy constants for Rewired, still used as IDs for Runner)
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
        internal static ConfigEntry<float> PanSpeed;
        internal static ConfigEntry<float> TiltSpeed;
        internal static ConfigEntry<float> ZoomSpeed;
        internal static ConfigEntry<float> AxisSensitivity;
        internal static ConfigEntry<float> MinFOV;
        internal static ConfigEntry<float> MaxFOV;
        internal static ConfigEntry<float> AutoLockMinDist;
        internal static ConfigEntry<float> AutoLockMaxDist;
        internal static ConfigEntry<bool>  ManualOnByDefault;

        // Keybinds (F1 menu)
        internal static ConfigEntry<KeyboardShortcut> KeyToggleManual;
        internal static ConfigEntry<KeyboardShortcut> KeyForceColor;
        internal static ConfigEntry<KeyboardShortcut> KeyReset;
        internal static ConfigEntry<KeyboardShortcut> KeyPanLeft;
        internal static ConfigEntry<KeyboardShortcut> KeyPanRight;
        internal static ConfigEntry<KeyboardShortcut> KeyTiltUp;
        internal static ConfigEntry<KeyboardShortcut> KeyTiltDown;
        internal static ConfigEntry<KeyboardShortcut> KeyZoomIn;
        internal static ConfigEntry<KeyboardShortcut> KeyZoomOut;

        // Runtime state
        internal static bool ManualMode = false;
        internal static float DesiredFOV = float.NaN;
        internal static Vector3 PanDir = Vector3.forward;
        internal static bool HasPanDir = false;
        internal static bool HasLastHit = false;
        internal static GlobalPosition LastHitGP;

        // Reflection cache
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

            // Tuning
            PanSpeed = Config.Bind("Tuning", "PanSpeedDegPerSec",  60f, "Pan rate when button input is held");
            TiltSpeed = Config.Bind("Tuning", "TiltSpeedDegPerSec", 60f, "Tilt rate when button input is held");
            ZoomSpeed = Config.Bind("Tuning", "ZoomSpeedDegPerSec", 30f, "FOV change rate (smaller FOV = more zoom)");
            AxisSensitivity = Config.Bind("Tuning", "AxisSensitivity", 1f, "Multiplier for analog Pan/Tilt/Zoom axes");
            MinFOV = Config.Bind("Tuning", "MinFOV", 0.25f, "Tightest zoom (smallest FOV in degrees)");
            MaxFOV = Config.Bind("Tuning", "MaxFOV", 80f,   "Widest view (largest FOV in degrees)");
            AutoLockMinDist = Config.Bind("Tuning", "AutoLockMinDist", 50f,    "Ignore raycast hits closer than this");
            AutoLockMaxDist = Config.Bind("Tuning", "AutoLockMaxDist", 100000f, "Maximum raycast distance");
            ManualOnByDefault = Config.Bind("Defaults", "ManualOnAtStart", false, "Start with manual pan/tilt enabled");
            ManualMode = ManualOnByDefault.Value;

            // Keybinds (F1 menu)
            KeyToggleManual = Config.Bind("Keybinds", "ToggleManualMode", new KeyboardShortcut(KeyCode.Delete), "Toggle manual camera control");
            KeyForceColor =   Config.Bind("Keybinds", "ForceColorMode",   new KeyboardShortcut(KeyCode.PageUp), "Force between IR and Color mode");
            KeyReset =        Config.Bind("Keybinds", "ResetView",        new KeyboardShortcut(KeyCode.Home), "Reset camera to forward and default zoom");
            KeyPanLeft =      Config.Bind("Keybinds", "PanLeft",          new KeyboardShortcut(KeyCode.I), "Pan camera left");
            KeyPanRight =     Config.Bind("Keybinds", "PanRight",         new KeyboardShortcut(KeyCode.P), "Pan camera right");
            KeyTiltUp =       Config.Bind("Keybinds", "TiltUp",           new KeyboardShortcut(KeyCode.O), "Tilt camera up");
            KeyTiltDown =     Config.Bind("Keybinds", "TiltDown",         new KeyboardShortcut(KeyCode.L), "Tilt camera down");
            KeyZoomIn =       Config.Bind("Keybinds", "ZoomIn",           new KeyboardShortcut(KeyCode.RightBracket), "Zoom camera in");
            KeyZoomOut =      Config.Bind("Keybinds", "ZoomOut",          new KeyboardShortcut(KeyCode.LeftBracket), "Zoom camera out");

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
            }
            catch (Exception e) { Log.LogError($"Reflection cache: {e}"); }

            new Harmony("com.noms.targetcamcontrol").PatchAll();

            SceneManager.sceneLoaded += (s, m) =>
            {
                if (Runner.Instance == null)
                {
                    var go = new GameObject("TargetCamControlRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<Runner>();
                }
            };

            Log.LogInfo($"Tactical Camera Controller v1.0.0 loaded. Configure keys in F1 menu.");
        }
    }

    [HarmonyPatch(typeof(InputManager_Base), "Awake")]
    static class Patch_RewiredAwake
    {
        [HarmonyPrefix]
        static void Prefix() { }
    }

    [HarmonyPatch(typeof(TargetScreenUI), "UpdateTargetInfo")]
    static class Patch_TargetScreenUI_UpdateInfo
    {
        [HarmonyPrefix]
        static bool Prefix(TargetScreenUI __instance)
        {
            if (!Plugin.ManualMode || !Plugin.HasLastHit) return true;
            try
            {
                var t = Traverse.Create(__instance);
                var targetList = t.Field("targetList").GetValue<List<Unit>>();
                if (targetList != null && targetList.Count > 0) return true;

                var hud = SceneSingleton<CombatHUD>.i;
                if (hud == null || hud.aircraft == null) return true;
                var aircraft = hud.aircraft;
                var targetCam = aircraft.targetCam;
                if (targetCam == null) return true;

                var displayingInfoF = t.Field("displayingInfo");
                bool displaying = displayingInfoF.GetValue<bool>();
                if (!displaying)
                {
                    var toggle = typeof(TargetScreenUI).GetMethod("ToggleInfoDisplay",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    toggle?.Invoke(__instance, new object[] { true });
                }

                Vector3 hitLocal = Plugin.LastHitGP.ToLocalPosition();
                GlobalPosition hitGP = Plugin.LastHitGP;
                GlobalPosition acGP = aircraft.GlobalPosition();
                Vector3 delta = hitGP - acGP;
                float dist = delta.magnitude;
                float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                if (bearing < 0f) bearing += 360f;
                float closure = -Vector3.Dot(aircraft.rb.velocity, delta.normalized);

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

                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(TargetCam), "SwitchIRState")]
    static class Patch_TargetCam_SwitchIRState
    {
        public static bool AllowNext = false;
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!Plugin.ManualMode) return true;
            if (AllowNext) { AllowNext = false; return true; }
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetCam), "AimCamera")]
    static class Patch_TargetCam_AimCamera
    {
        [HarmonyPrefix]
        static bool Prefix() => !Plugin.ManualMode;
    }

    [HarmonyPatch(typeof(TargetCam), "Update")]
    static class Patch_TargetCam_Update
    {
        [HarmonyPrefix]
        static bool Prefix(TargetCam __instance)
        {
            if (!Plugin.ManualMode) return true;
            try
            {
                var lastExpField = typeof(TargetCam).GetField("lastExposureUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (lastExpField != null)
                {
                    float last = (float)lastExpField.GetValue(__instance);
                    if (Time.timeSinceLevelLoad - last > 1f)
                    {
                        var m = typeof(TargetCam).GetMethod("UpdateExposure", BindingFlags.NonPublic | BindingFlags.Instance);
                        m?.Invoke(__instance, null);
                        lastExpField.SetValue(__instance, Time.timeSinceLevelLoad);
                    }
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(LaserDesignator), "Update")]
    static class Patch_LaserDesignator_WeaponOverride
    {
        [HarmonyPostfix]
        static void Postfix(LaserDesignator __instance)
        {
            if (!Plugin.ManualMode) return;
            try
            {
                var tInstance = Traverse.Create(__instance);
                var cam = tInstance.Field("cam").GetValue<Camera>();
                Transform tgpTransform = cam != null ? cam.transform : null;
                if (tgpTransform == null) return;

                if (Physics.Raycast(tgpTransform.position, tgpTransform.forward, out RaycastHit hit, 100000f))
                {
                    if (tInstance.Field("laserImpactPoint").FieldExists())
                        tInstance.Field("laserImpactPoint").SetValue(hit.point);
                    if (tInstance.Field("currentTargetPosition").FieldExists())
                        tInstance.Field("currentTargetPosition").SetValue(hit.point);
                }
            }
            catch { }
        }
    }
}
