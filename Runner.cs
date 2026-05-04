using System;
using System.Reflection;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace TargetCamControl
{
    /// <summary>
    /// Manual Mode: ON/OFF only.
    /// While ON, raycast from camera forward each frame; if it hits the world
    /// (excluding the player aircraft), automatically point the camera at that
    /// hit point. User pan/tilt steers where to raycast.
    /// </summary>
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

        void Update()
        {
            try
            {
                var p = ReInput.players?.GetPlayer(PLAYER_ID);
                if (p == null) return;

                bool tm = p.GetButton(Plugin.ACT_TOGGLE_MAN);
                if (tm && !toggleManPrev)
                {
                    if (Plugin.ManualMode) ExitManual();
                    else                   EnterManual();
                }
                toggleManPrev = tm;

                bool fc = p.GetButton(Plugin.ACT_FORCE_COL);
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
                            // Bypass our own block so this manual toggle goes through.
                            Patch_TargetCam_SwitchIRState.AllowNext = true;
                            m.Invoke(t, new object[] { !current });
                            Plugin.Log.LogInfo($"[TCC] IR toggle: {(!current ? "IR" : "COLOR")}");
                        }
                        else
                        {
                            // Fallback: set field directly
                            Plugin.F_TargetCam_IRMode.SetValue(t, !current);
                        }
                    }
                }
                forceColPrev = fc;
            }
            catch { /* Rewired not ready */ }
        }

        void LateUpdate()
        {
            try
            {
                var p = ReInput.players?.GetPlayer(PLAYER_ID);
                if (p == null) return;
                var tc = FindPlayerTargetCam();
                if (tc == null) return;

                // Auto-exit manual when game gets a real target
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

                // ---- Pan/Tilt input ----
                bool pl = p.GetButton(Plugin.ACT_PAN_LEFT);
                bool pr = p.GetButton(Plugin.ACT_PAN_RIGHT);
                bool tu = p.GetButton(Plugin.ACT_TILT_UP);
                bool td = p.GetButton(Plugin.ACT_TILT_DOWN);
                float pa = p.GetAxis(Plugin.ACT_PAN_AXIS);
                float ta = p.GetAxis(Plugin.ACT_TILT_AXIS);

                // Zoom-aware sensitivity. Linearly proportional to FOV with a tiny floor
                // so 40x zoom (FOV 0.25°) gives ~0.5°/sec — fine enough for tap-tap aiming.
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
                if (Mathf.Abs(pa) > 0.05f) panDelta  += pa * panRate  * Plugin.AxisSensitivity.Value * dt;
                if (Mathf.Abs(ta) > 0.05f) tiltDelta += ta * tiltRate * Plugin.AxisSensitivity.Value * dt;

                // ---- Zoom input ----
                bool zi = p.GetButton(Plugin.ACT_ZOOM_IN);
                bool zo = p.GetButton(Plugin.ACT_ZOOM_OUT);
                float zaxis = p.GetAxis(Plugin.ACT_ZOOM_AXIS);
                float zoomDelta = 0f;
                if (zi) zoomDelta -= Plugin.ZoomSpeed.Value * dt;
                if (zo) zoomDelta += Plugin.ZoomSpeed.Value * dt;
                if (Mathf.Abs(zaxis) > 0.05f) zoomDelta += -zaxis * Plugin.ZoomSpeed.Value * Plugin.AxisSensitivity.Value * dt;
                if (zoomDelta != 0f && Plugin.F_TargetCam_targetFOV != null)
                {
                    if (float.IsNaN(Plugin.DesiredFOV))
                        Plugin.DesiredFOV = (float)Plugin.F_TargetCam_targetFOV.GetValue(tc);
                    if (float.IsNaN(Plugin.DesiredFOV) || Plugin.DesiredFOV <= 0f) Plugin.DesiredFOV = 10f;
                    Plugin.DesiredFOV = Mathf.Clamp(Plugin.DesiredFOV + zoomDelta,
                        Plugin.MinFOV.Value, Plugin.MaxFOV.Value);
                }

                // ---- Reset ----
                bool rv = p.GetButton(Plugin.ACT_RESET);
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

                // FOV
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

                bool hasInput = (panDelta != 0f) || (tiltDelta != 0f);

                // Initialize PanDir on first frame after EnterManual: use lock direction if
                // available, otherwise aircraft forward. World-space, independent of bank/yaw.
                if (!Plugin.HasPanDir)
                {
                    if (Plugin.HasLastHit)
                    {
                        Vector3 hitLocal0 = Plugin.LastHitGP.ToLocalPosition();
                        Plugin.PanDir = (hitLocal0 - mount.position).normalized;
                    }
                    else
                    {
                        Plugin.PanDir = aircraft.transform.forward;
                    }
                    Plugin.HasPanDir = true;
                }

                if (hasInput)
                {
                    // Apply pan/tilt as world-axis deltas to the persistent direction vector.
                    // This way the camera doesn't tumble with aircraft bank/yaw and the user
                    // can pan sideways while the aircraft is maneuvering.
                    if (Mathf.Abs(panDelta) > 0f)
                        Plugin.PanDir = Quaternion.AngleAxis(panDelta, Vector3.up) * Plugin.PanDir;
                    if (Mathf.Abs(tiltDelta) > 0f)
                    {
                        Vector3 right = Vector3.Cross(Vector3.up, Plugin.PanDir).normalized;
                        if (right.sqrMagnitude > 0.001f)
                            Plugin.PanDir = Quaternion.AngleAxis(tiltDelta, right) * Plugin.PanDir;
                    }
                    Plugin.PanDir = Plugin.PanDir.normalized;

                    // Apply cam rotation in world space — independent of aircraft attitude.
                    mount.rotation = Quaternion.LookRotation(Plugin.PanDir, Vector3.up);

                    // Raycast in that direction so LastHit is fresh when input releases.
                    Vector3 origin = camComp.transform.position + Plugin.PanDir * 50f;
                    if (TryRaycastWorld(origin, Plugin.PanDir, aircraft, out Vector3 hit, out string hitName))
                    {
                        Plugin.LastHitGP = hit.ToGlobalPosition();
                        Plugin.HasLastHit = true;
                    }
                }
                else
                {
                    // No input — track the locked world point. As aircraft moves, the look
                    // angle auto-adjusts to keep the same point centered.
                    if (Plugin.HasLastHit)
                    {
                        Vector3 hitLocal = Plugin.LastHitGP.ToLocalPosition();
                        Vector3 toHit = hitLocal - mount.position;
                        if (toHit.sqrMagnitude > 0.01f)
                        {
                            Plugin.PanDir = toHit.normalized;
                            mount.rotation = Quaternion.LookRotation(Plugin.PanDir, Vector3.up);
                        }
                    }
                    else
                    {
                        mount.rotation = Quaternion.LookRotation(Plugin.PanDir, Vector3.up);
                    }
                }
                if (camComp.transform.localRotation != Quaternion.identity)
                    camComp.transform.localRotation = Quaternion.identity;

                EnsureCrosshair(tc);
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0) Plugin.Log.LogWarning($"[TCC] LateUpdate: {e.Message}");
            }
        }

        // ---- Raycast that ignores the player aircraft's colliders ----
        // Filters via two checks because some parts (separated cockpit, weapon racks)
        // aren't always children of aircraft.transform but DO have a UnitPart whose
        // parentUnit is our aircraft.
        private static readonly RaycastHit[] _hitBuf = new RaycastHit[64];
        private static bool TryRaycastWorld(Vector3 origin, Vector3 dir, Aircraft aircraft, out Vector3 point, out string hitName)
        {
            point = default;
            hitName = null;
            float minDist = Plugin.AutoLockMinDist?.Value ?? 500f;
            float maxDist = Plugin.AutoLockMaxDist?.Value ?? 100000f;
            int n = Physics.RaycastNonAlloc(origin, dir, _hitBuf, maxDist);
            if (n == 0) return false;

            Transform acRoot = aircraft != null ? aircraft.transform : null;
            float bestDist = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < n; i++)
            {
                var h = _hitBuf[i];
                if (h.collider == null) continue;
                // Skip foreground hits (foothills, near terrain) — user typically wants distant targets
                if (h.distance < minDist) continue;
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
            hitName = _hitBuf[bestIdx].collider.name;
            return true;
        }

        // ---- State transitions ----

        private static float NormalizeAngle(float a)
        {
            a = a % 360f;
            if (a > 180f) a -= 360f;
            return a;
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

            // Reset transforms first
            var camComp = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
            if (camComp != null) camComp.transform.localRotation = Quaternion.identity;
            var mount = Plugin.F_TargetCam_currentMount?.GetValue(tc) as Transform;
            if (mount != null) mount.localRotation = Quaternion.identity;

            // Disable the cam so the MFD reverts to the normal radar/TacScreen view.
            // Calling CancelTarget invokes the onCamToggle event which game listeners use
            // to hide the targetCamDisplay overlay properly.
            try
            {
                var cancel = typeof(TargetCam).GetMethod("CancelTarget",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                cancel?.Invoke(tc, null);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[TCC] CancelTarget: {e.Message}"); }
        }

        // ---- Helpers ----

        // ---- Hit-point crosshair ----
        // The cam already points at LastHit, so the hit is at screen center. Show a
        // small "[X]" reticle at the canvas center as visual confirmation.
        private void EnsureCrosshair(TargetCam tc)
        {
            try
            {
                if (!Plugin.HasLastHit)
                {
                    DestroyCrosshair();
                    return;
                }
                if (crosshairGO != null && crosshairGO.activeSelf) return;

                // Find the canvasObjectTarget instantiated by SetTargetCam
                var canvasGO = Plugin.F_TargetCam_canvasObjectTarget?.GetValue(tc) as GameObject;
                if (canvasGO == null) return;
                var canvas = canvasGO.GetComponentInChildren<Canvas>();
                if (canvas == null) return;

                if (crosshairGO == null)
                {
                    crosshairGO = new GameObject("TCC_Crosshair");
                    crosshairGO.transform.SetParent(canvas.transform, false);
                    var rt = crosshairGO.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(80, 80);

                    var txt = crosshairGO.AddComponent<Text>();
                    txt.text = "[X]";
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.fontSize = 28;
                    txt.color = new Color(0f, 1f, 0.4f, 0.85f);
                    txt.raycastTarget = false;
                    Font font = null;
                    try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch {}
                    if (font == null) { try { font = Font.CreateDynamicFontFromOSFont("Arial", 28); } catch {} }
                    if (font != null) txt.font = font;
                }
                crosshairGO.SetActive(true);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[TCC] EnsureCrosshair: {e.Message}"); }
        }

        private void DestroyCrosshair()
        {
            if (crosshairGO != null) { UnityEngine.Object.Destroy(crosshairGO); crosshairGO = null; }
        }

        void OnDestroy() { DestroyCrosshair(); }

        private static TargetCam FindPlayerTargetCam()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                if (hud == null) return null;
                var aircraft = hud.aircraft;
                return aircraft != null ? aircraft.targetCam : null;
            }
            catch { return null; }
        }

        private static bool HasGameTarget()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                var aircraft = hud?.aircraft;
                var wm = aircraft?.weaponManager;
                var list = wm?.GetTargetList();
                return list != null && list.Count > 0;
            }
            catch { return false; }
        }

        private static void ForceEnableManualCam(TargetCam tc)
        {
            try
            {
                var camComp = Plugin.F_TargetCam_cam?.GetValue(tc) as Camera;
                if (camComp == null) return;
                if (!camComp.enabled)
                    Plugin.M_TargetCam_SetTargetCam?.Invoke(tc, null);

                if (Plugin.F_TargetCam_targetFOV != null)
                {
                    float curFOV = (float)Plugin.F_TargetCam_targetFOV.GetValue(tc);
                    if (float.IsNaN(curFOV) || curFOV <= 0f)
                        Plugin.F_TargetCam_targetFOV.SetValue(tc, 10f);
                }
                if (Plugin.F_TargetCam_camTimeout != null)
                    Plugin.F_TargetCam_camTimeout.SetValue(tc, 99f);

                // Force currentMount to forward (rear mount has 180° yaw which inverts
                // pan/tilt math). Re-parent cam to it and zero everything.
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
            catch (Exception e) { Plugin.Log.LogError($"[TCC] ForceEnableManualCam: {e.Message}"); }
        }
    }
}
