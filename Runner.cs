using System;
using System.Reflection;
using Rewired;
using UnityEngine;
using UnityEngine.UI;
using BepInEx.Configuration;

namespace TargetCamControl
{
    public class Runner : MonoBehaviour
    {
        public static Runner Instance;
        private static readonly int PLAYER_ID = 0;

        private bool toggleManPrev = false;
        private bool forceColPrev = false;
        private bool prevHadTarget = false;
        private bool _prevReset = false;
        private GameObject crosshairGO;

        void Awake() { Instance = this; }

        private bool GetKeyDown(ConfigEntry<KeyboardShortcut> config)
        {
            if (config == null) return false;
            return config.Value.IsDown();
        }

        private bool GetKey(ConfigEntry<KeyboardShortcut> config)
        {
            if (config == null) return false;
            return config.Value.IsPressed();
        }

        void Update()
        {
            try
            {
                // Toggle Manual Mode
                bool tm = GetKeyDown(Plugin.KeyToggleManual);
                if (tm && !toggleManPrev)
                {
                    if (Plugin.ManualMode) ExitManual();
                    else                   EnterManual();
                }
                toggleManPrev = tm;

                // Force Color/IR
                bool fc = GetKeyDown(Plugin.KeyForceColor);
                if (fc && !forceColPrev)
                {
                    var t = FindPlayerTargetCam();
                    if (t != null && Plugin.F_TargetCam_IRMode != null)
                    {
                        bool current = (bool)Plugin.F_TargetCam_IRMode.GetValue(t);
                        var m = typeof(TargetCam).GetMethod("SwitchIRState",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (m != null)
                        {
                            Patch_TargetCam_SwitchIRState.AllowNext = true;
                            m.Invoke(t, new object[] { !current });
                            Plugin.Log.LogInfo($"[TCC] IR toggle: {(!current ? "IR" : "COLOR")}");
                        }
                    }
                }
                forceColPrev = fc;
            }
            catch (Exception e) { Plugin.Log.LogError($"[TCC] Update: {e}"); }
        }

        void LateUpdate()
        {
            try
            {
                var tc = FindPlayerTargetCam();
                if (tc == null) return;

                bool hasTarget = HasGameTarget();
                if (hasTarget && !prevHadTarget && Plugin.ManualMode)
                {
                    Plugin.Log.LogInfo("[TCC] target acquired — exiting manual");
                    ExitManual();
                }
                prevHadTarget = hasTarget;

                if (!Plugin.ManualMode)
                {
                    Plugin.DesiredFOV = float.NaN;
                    return;
                }

                float dt = Time.unscaledDeltaTime;

                // ---- Pan/Tilt input (Keyboard) ----
                bool pl = GetKey(Plugin.KeyPanLeft);
                bool pr = GetKey(Plugin.KeyPanRight);
                bool tu = GetKey(Plugin.KeyTiltUp);
                bool td = GetKey(Plugin.KeyTiltDown);

                // Zoom-aware sensitivity
                var camComp0 = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
                float curFOV = camComp0 != null ? camComp0.fieldOfView : 30f;
                float zoomScale = Mathf.Clamp(curFOV / 30f, 0.005f, 1.5f);
                float panRate  = Plugin.PanSpeed.Value  * zoomScale;
                float tiltRate = Plugin.TiltSpeed.Value * zoomScale;

                float panDelta = 0f, tiltDelta = 0f;
                if (pl) panDelta  -= panRate * dt;
                if (pr) panDelta  += panRate * dt;
                if (tu) tiltDelta -= tiltRate * dt;
                if (td) tiltDelta += tiltRate * dt;

                // ---- Zoom input (Keyboard) ----
                bool zi = GetKey(Plugin.KeyZoomIn);
                bool zo = GetKey(Plugin.KeyZoomOut);
                float zoomDelta = 0f;
                if (zi) zoomDelta -= Plugin.ZoomSpeed.Value * dt;
                if (zo) zoomDelta += Plugin.ZoomSpeed.Value * dt;
                
                if (zoomDelta != 0f && Plugin.F_TargetCam_targetFOV != null)
                {
                    if (float.IsNaN(Plugin.DesiredFOV))
                        Plugin.DesiredFOV = (float)Plugin.F_TargetCam_targetFOV.GetValue(tc);
                    if (float.IsNaN(Plugin.DesiredFOV) || Plugin.DesiredFOV <= 0f) Plugin.DesiredFOV = 10f;
                    Plugin.DesiredFOV = Mathf.Clamp(Plugin.DesiredFOV + zoomDelta,
                        Plugin.MinFOV.Value, Plugin.MaxFOV.Value);
                }

                // ---- Reset ----
                bool rv = GetKeyDown(Plugin.KeyReset);
                if (rv && !_prevReset)
                {
                    Plugin.DesiredFOV = float.NaN;
                    Plugin.HasLastHit = false;
                    Plugin.HasPanDir = false;
                    Plugin.Log.LogInfo("[TCC] view reset");
                }
                _prevReset = rv;

                var camComp = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
                var mount = Plugin.F_TargetCam_currentMount?.GetValue(tc) as Transform;
                if (camComp == null || mount == null) return;

                if (!float.IsNaN(Plugin.DesiredFOV))
                {
                    camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, Plugin.DesiredFOV, dt * 6f);
                    if (Plugin.F_TargetCam_targetFOV != null)
                        Plugin.F_TargetCam_targetFOV.SetValue(tc, Plugin.DesiredFOV);
                }
                if (Plugin.F_TargetCam_camTimeout != null)
                    Plugin.F_TargetCam_camTimeout.SetValue(tc, 99f);

                var aircraft = SceneSingleton<CombatHUD>.i?.aircraft;
                if (aircraft == null) return;

                if (!Plugin.HasPanDir)
                {
                    Plugin.PanDir = aircraft.transform.forward;
                    Plugin.HasPanDir = true;
                }

                if (Plugin.HasLastHit)
                {
                    Vector3 hitLocal = Plugin.LastHitGP.ToLocalPosition();
                    Vector3 toHit = hitLocal - mount.position;
                    if (toHit.sqrMagnitude > 0.01f)
                        Plugin.PanDir = toHit.normalized;
                }

                if (panDelta != 0f || tiltDelta != 0f)
                {
                    if (Mathf.Abs(panDelta) > 0f)
                        Plugin.PanDir = Quaternion.AngleAxis(panDelta, Vector3.up) * Plugin.PanDir;
                    if (Mathf.Abs(tiltDelta) > 0f)
                    {
                        Vector3 right = Vector3.Cross(Vector3.up, Plugin.PanDir).normalized;
                        if (right.sqrMagnitude > 0.001f)
                            Plugin.PanDir = Quaternion.AngleAxis(tiltDelta, right) * Plugin.PanDir;
                    }
                    Plugin.PanDir = Plugin.PanDir.normalized;

                    Vector3 origin = camComp.transform.position + Plugin.PanDir * 50f;
                    if (TryRaycastWorld(origin, Plugin.PanDir, aircraft, out Vector3 hit))
                    {
                        Plugin.LastHitGP = hit.ToGlobalPosition();
                        Plugin.HasLastHit = true;
                    }
                }

                mount.rotation = Quaternion.LookRotation(Plugin.PanDir, Vector3.up);
                if (camComp.transform.localRotation != Quaternion.identity)
                    camComp.transform.localRotation = Quaternion.identity;

                EnsureCrosshair(tc);
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0) Plugin.Log.LogWarning($"[TCC] LateUpdate: {e.Message}");
            }
        }

        private static readonly RaycastHit[] _hitBuf = new RaycastHit[64];
        private static bool TryRaycastWorld(Vector3 origin, Vector3 dir, Aircraft aircraft, out Vector3 point)
        {
            point = default;
            float minDist = Plugin.AutoLockMinDist?.Value ?? 50f;
            float maxDist = Plugin.AutoLockMaxDist?.Value ?? 100000f;
            int n = Physics.RaycastNonAlloc(origin, dir, _hitBuf, maxDist);
            if (n == 0) return false;

            Transform acRoot = aircraft != null ? aircraft.transform : null;
            float bestDist = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < n; i++)
            {
                var h = _hitBuf[i];
                if (h.collider == null || h.distance < minDist) continue;
                var t = h.collider.transform;

                if (acRoot != null && t.IsChildOf(acRoot)) continue;
                var part = h.collider.GetComponentInParent<UnitPart>();
                if (part != null && part.parentUnit != null && (object)part.parentUnit == (object)aircraft) continue;
                var unit = h.collider.GetComponentInParent<Unit>();
                if (unit != null && (object)unit == (object)aircraft) continue;

                if (h.distance < bestDist) { bestDist = h.distance; bestIdx = i; }
            }
            if (bestIdx < 0) return false;
            point = _hitBuf[bestIdx].point;
            return true;
        }

        private void EnterManual()
        {
            Plugin.ManualMode = true;
            Plugin.HasLastHit = false;
            Plugin.HasPanDir = false;
            Plugin.Log.LogInfo("[TCC] Manual: ON");
            var tc = FindPlayerTargetCam();
            if (tc != null) ForceEnableManualCam(tc);
        }

        private void ExitManual()
        {
            Plugin.ManualMode = false;
            Plugin.DesiredFOV = float.NaN;
            Plugin.HasLastHit = false;
            Plugin.HasPanDir = false;
            Plugin.Log.LogInfo("[TCC] Manual: OFF");

            var tc = FindPlayerTargetCam();
            if (tc == null) return;

            var camComp = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
            if (camComp != null) camComp.transform.localRotation = Quaternion.identity;
            var mount = Plugin.F_TargetCam_currentMount?.GetValue(tc) as Transform;
            if (mount != null) mount.localRotation = Quaternion.identity;

            try
            {
                var cancel = typeof(TargetCam).GetMethod("CancelTarget",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                cancel?.Invoke(tc, null);
            }
            catch { }
        }

        private void EnsureCrosshair(TargetCam tc)
        {
            try
            {
                if (!Plugin.HasLastHit) { DestroyCrosshair(); return; }
                if (crosshairGO != null && crosshairGO.activeSelf) return;

                var canvasGO = Plugin.F_TargetCam_canvasObjectTarget?.GetValue(tc) as GameObject;
                if (canvasGO == null) return;
                var canvas = canvasGO.GetComponentInChildren<Canvas>();
                if (canvas == null) return;

                if (crosshairGO == null)
                {
                    crosshairGO = new GameObject("TCC_Crosshair");
                    crosshairGO.transform.SetParent(canvas.transform, false);
                    var rt = crosshairGO.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(80, 80);

                    var txt = crosshairGO.AddComponent<Text>();
                    txt.text = "[X]";
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.fontSize = 28;
                    txt.color = new Color(0f, 1f, 0.4f, 0.85f);
                    txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                crosshairGO.SetActive(true);
            }
            catch { }
        }

        private void DestroyCrosshair()
        {
            if (crosshairGO != null) { UnityEngine.Object.Destroy(crosshairGO); crosshairGO = null; }
        }

        void OnDestroy() { DestroyCrosshair(); }

        private static TargetCam FindPlayerTargetCam()
        {
            try { return SceneSingleton<CombatHUD>.i?.aircraft?.targetCam; } catch { return null; }
        }

        private static bool HasGameTarget()
        {
            try { var list = SceneSingleton<CombatHUD>.i?.aircraft?.weaponManager?.GetTargetList(); return list != null && list.Count > 0; } catch { return false; }
        }

        private static void ForceEnableManualCam(TargetCam tc)
        {
            try
            {
                var camComp = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
                if (camComp == null) return;

                if (Plugin.F_TargetCam_currentMode != null)
                    Plugin.F_TargetCam_currentMode.SetValue(tc, 0);
                var landingCanvas = Plugin.F_TargetCam_canvasObjectLanding?.GetValue(tc) as GameObject;
                if (landingCanvas != null) landingCanvas.SetActive(false);

                if (!camComp.enabled)
                    Plugin.M_TargetCam_SetTargetCam?.Invoke(tc, null);

                if (Plugin.F_TargetCam_targetFOV != null)
                    Plugin.F_TargetCam_targetFOV.SetValue(tc, 10f);
                if (Plugin.F_TargetCam_camTimeout != null)
                    Plugin.F_TargetCam_camTimeout.SetValue(tc, 99f);

                var mountFwd = Plugin.F_TargetCam_camMountForward?.GetValue(tc) as Transform;
                if (mountFwd != null)
                {
                    mountFwd.localEulerAngles = Vector3.zero;
                    if (Plugin.F_TargetCam_currentMount != null)
                        Plugin.F_TargetCam_currentMount.SetValue(tc, mountFwd);
                    if (camComp.transform.parent != mountFwd)
                        camComp.transform.parent = mountFwd;
                }
                camComp.transform.localPosition = Vector3.zero;
                camComp.transform.localRotation = Quaternion.identity;
                Plugin.DesiredFOV = 10f;
            }
            catch { }
        }
    }
}
