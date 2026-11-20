using System;
using System.Globalization;
using System.Text;
using Apollo;
using Apollo.LunarModule;
using Apollo.Maneuvers;
using HarmonyLib;
using UnityEngine;

namespace ReentrySync.Core
{
    public enum AutopilotMode
    {
        OFF,
        KILL_ROT,
        PROGRADE,
        RETROGRADE,
        NORMAL,
        ANTI_NORMAL,
        RADIAL_IN,
        RADIAL_OUT,
        AUTO_BURN,
        AUTO_DOCK,
        AUTO_LAND,
        AUTO_ASCENT
    }

    public static class FlightAutopilot
    {
        public static AutopilotMode Mode { get; private set; } = AutopilotMode.OFF;
        public static string PhaseDetail { get; private set; } = "Standby";
        public static float AttitudeErrorDeg { get; private set; } = 0f;
        public static float BurnDvRemainingFps { get; private set; } = 0f;
        public static float BurnTigRemainingSec { get; private set; } = 0f;
        public static float DockDistanceMeters { get; private set; } = 0f;
        public static float TargetAscentOrbitNm { get; set; } = 60f;
        public static bool BurnImmediate { get; set; } = false;

        private static bool _burnActive = false;
        private static bool _burnUllageStarted = false;
        private static float _burnStartTime = 0f;
        private static bool _ascentStaged = false;
        private static bool _gearDeployed = false;

        // Reflection accessors for private/internal transforms & rigidbodies
        private static readonly AccessTools.FieldRef<ProApolloRocket, Rigidbody>? _csmRbRef =
            AccessTools.FieldRefAccess<ProApolloRocket, Rigidbody>("rb");

        private static readonly AccessTools.FieldRef<ProApolloRocket, Transform>? _csmProbeRef =
            AccessTools.FieldRefAccess<ProApolloRocket, Transform>("dockingProbeDirection");

        private static readonly AccessTools.FieldRef<ProApolloRocket, NBody>? _csmBodyRef =
            AccessTools.FieldRefAccess<ProApolloRocket, NBody>("physicsBody");

        private static readonly AccessTools.FieldRef<LunarModuleController, Rigidbody>? _lmRbRef =
            AccessTools.FieldRefAccess<LunarModuleController, Rigidbody>("rb");

        private static readonly AccessTools.FieldRef<LunarModuleController, Rigidbody>? _lmStayRbRef =
            AccessTools.FieldRefAccess<LunarModuleController, Rigidbody>("rbLunarStay");

        private static readonly AccessTools.FieldRef<LunarModuleController, Transform>? _lmConeRef =
            AccessTools.FieldRefAccess<LunarModuleController, Transform>("dockingConeDirection");

        private static readonly AccessTools.FieldRef<LunarModuleLandingController, Rigidbody>? _lmLandRbRef =
            AccessTools.FieldRefAccess<LunarModuleLandingController, Rigidbody>("rb");

        private static readonly AccessTools.FieldRef<LunarModuleLandingController, GameObject>? _lmFlagRef =
            AccessTools.FieldRefAccess<LunarModuleLandingController, GameObject>("landingTargetFlag");

        private static readonly AccessTools.FieldRef<ApolloProLogicHandler, LunarModuleController>? _logicLmRef =
            AccessTools.FieldRefAccess<ApolloProLogicHandler, LunarModuleController>("lunarModule");

        private static readonly AccessTools.FieldRef<ApolloProLogicHandler, LunarModuleLandingController>? _logicLmLandRef =
            AccessTools.FieldRefAccess<ApolloProLogicHandler, LunarModuleLandingController>("lunarModuleLandingController");

        private static readonly Action<ProApolloRocket, float, Vector3>? _applyTransThrust =
            AccessTools.MethodDelegate<Action<ProApolloRocket, float, Vector3>>(
                AccessTools.Method(typeof(ProApolloRocket), "ApplyTranslationalThrust"));

        private static readonly Func<LunarModuleController, float>? _checkDockDist =
            AccessTools.MethodDelegate<Func<LunarModuleController, float>>(
                AccessTools.Method(typeof(LunarModuleController), "CheckDockingDistance"));

        private static readonly Func<LunarModuleController, bool>? _latchToDock =
            AccessTools.MethodDelegate<Func<LunarModuleController, bool>>(
                AccessTools.Method(typeof(LunarModuleController), "LatchToDock"));

        public static void SetMode(AutopilotMode newMode, bool immediate = false)
        {
            // Clean up active engines if disengaging
            if (newMode == AutopilotMode.OFF)
            {
                SafeCutoffEngines();
            }

            Mode = newMode;
            BurnImmediate = immediate;
            _burnActive = false;
            _burnUllageStarted = false;
            _ascentStaged = false;
            _gearDeployed = false;

            if (newMode == AutopilotMode.AUTO_BURN)
            {
                if (BurnPlannerBridge.CurrentPlan.Valid)
                {
                    BurnDvRemainingFps = BurnPlannerBridge.CurrentPlan.DeltaVFps;
                    BurnTigRemainingSec = immediate ? 3f : Math.Max(3f, BurnPlannerBridge.CurrentPlan.TimeToIgnitionSec);
                    PhaseDetail = $"Armed: {BurnPlannerBridge.CurrentPlan.Name} (ΔV {BurnDvRemainingFps:0.1} ft/s)";
                }
                else
                {
                    Mode = AutopilotMode.OFF;
                    PhaseDetail = "No valid burn plan! Create a burn in the Burn Planner first.";
                }
            }
            else if (newMode == AutopilotMode.OFF)
            {
                PhaseDetail = "Autopilot disengaged — manual control.";
                AttitudeErrorDeg = 0f;
            }
            else
            {
                PhaseDetail = $"Engaged {newMode}";
            }
        }

        public static bool AlignPlatformsNow(out string msg)
        {
            msg = "";
            var logic = ApolloProLogicHandler.Instance;
            if (logic == null || State.Current == null)
            {
                msg = "No active Apollo flight.";
                return false;
            }

            try
            {
                var gmt = UnityEngine.Object.FindAnyObjectByType<GenericOrbitalMechanicsTool>();
                if (State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule)
                {
                    gmt?.SetNominalREFSMMAT_LM();
                    logic.SetLandingpoint(true);
                    msg = "LM IMU & REFSMMAT aligned to lunar landing site / orbital plane!";
                }
                else
                {
                    gmt?.SetNominalREFSMMAT_CSM();
                    if (logic.apolloRocket != null)
                    {
                        logic.apolloRocket.AlignGDCPlatformToBody(logic.apolloRocket.transform);
                    }
                    msg = "CSM IMU, GDC & REFSMMAT aligned to nominal flight reference!";
                }
                PhaseDetail = msg;
                return true;
            }
            catch (Exception ex)
            {
                msg = $"Alignment error: {ex.Message}";
                return false;
            }
        }

        public static void FixedTick()
        {
            if (Mode == AutopilotMode.OFF || State.Current == null)
                return;

            try
            {
                switch (Mode)
                {
                    case AutopilotMode.KILL_ROT:
                        RunKillRotation();
                        break;

                    case AutopilotMode.PROGRADE:
                    case AutopilotMode.RETROGRADE:
                    case AutopilotMode.NORMAL:
                    case AutopilotMode.ANTI_NORMAL:
                    case AutopilotMode.RADIAL_IN:
                    case AutopilotMode.RADIAL_OUT:
                        RunOrbitalAttitudeHold(Mode);
                        break;

                    case AutopilotMode.AUTO_BURN:
                        RunAutoBurn();
                        break;

                    case AutopilotMode.AUTO_DOCK:
                        RunAutoDock();
                        break;

                    case AutopilotMode.AUTO_LAND:
                        RunAutoLunarLanding();
                        break;

                    case AutopilotMode.AUTO_ASCENT:
                        RunAutoLunarAscent();
                        break;
                }
            }
            catch (Exception ex)
            {
                PhaseDetail = $"Autopilot notice: {ex.Message}";
            }
        }

        // -------------------------------------------------------------
        // Active Craft Helpers
        // -------------------------------------------------------------
        private static Rigidbody? GetActiveRigidbody()
        {
            var logic = ApolloProLogicHandler.Instance;
            if (logic == null) return null;

            if (State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule)
            {
                var landCtrl = _logicLmLandRef?.Invoke(logic);
                if (landCtrl != null && landCtrl.gameObject.activeInHierarchy)
                {
                    var landRb = _lmLandRbRef?.Invoke(landCtrl);
                    if (landRb != null && !landRb.isKinematic) return landRb;
                }

                var lm = _logicLmRef?.Invoke(logic);
                if (lm != null)
                {
                    var stayRb = _lmStayRbRef?.Invoke(lm);
                    if (stayRb != null && stayRb.gameObject.activeInHierarchy && !stayRb.isKinematic)
                        return stayRb;

                    var spaceRb = _lmRbRef?.Invoke(lm);
                    if (spaceRb != null) return spaceRb;
                }
            }

            if (logic.apolloRocket != null && _csmRbRef != null)
            {
                return _csmRbRef(logic.apolloRocket);
            }

            return null;
        }

        private static bool GetOrbitalBasis(out Vector3 prograde, out Vector3 radialOut, out Vector3 normal)
        {
            prograde = Vector3.forward;
            radialOut = Vector3.up;
            normal = Vector3.right;

            var cur = State.Current;
            if (cur == null) return false;

            Vector3 vel = cur.Velocity != null ? cur.Velocity.VelocityVector : Vector3.zero;
            Vector3 grav = cur.Forces != null ? cur.Forces.GravityVector : Vector3.down;

            if (State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule && cur.LunarModuleState != null)
            {
                if (cur.LunarModuleState.Velocity != null && cur.LunarModuleState.Velocity.VelocityVector.sqrMagnitude > 0.001f)
                    vel = cur.LunarModuleState.Velocity.VelocityVector;
                if (cur.LunarModuleState.Forces != null && cur.LunarModuleState.Forces.GravityVector.sqrMagnitude > 0.001f)
                    grav = cur.LunarModuleState.Forces.GravityVector;
            }

            if (vel.sqrMagnitude < 1e-6f) return false;

            prograde = vel.normalized;
            radialOut = (-grav).sqrMagnitude > 1e-6f ? (-grav).normalized : Vector3.up;
            normal = Vector3.Cross(prograde, radialOut).normalized;
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.right;
            radialOut = Vector3.Cross(normal, prograde).normalized;
            return true;
        }

        private static float SlewToward(Rigidbody rb, Vector3 desiredForward, Vector3 desiredUp, float kp = 2.6f, float kd = 3.2f)
        {
            if (rb == null || desiredForward.sqrMagnitude < 1e-5f) return 0f;

            desiredForward.Normalize();
            if (desiredUp.sqrMagnitude < 1e-5f || Math.Abs(Vector3.Dot(desiredForward, desiredUp.normalized)) > 0.98f)
            {
                desiredUp = Vector3.Cross(desiredForward, Vector3.right);
                if (desiredUp.sqrMagnitude < 1e-4f) desiredUp = Vector3.Cross(desiredForward, Vector3.up);
            }
            desiredUp.Normalize();

            Quaternion targetRot = Quaternion.LookRotation(desiredForward, desiredUp);
            Quaternion deltaRot = targetRot * Quaternion.Inverse(rb.rotation);
            deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);

            if (angleDeg > 180f) angleDeg -= 360f;
            AttitudeErrorDeg = Mathf.Abs(angleDeg);

            Vector3 torque = Vector3.zero;
            if (Mathf.Abs(angleDeg) > 0.05f && axis.sqrMagnitude > 1e-5f)
            {
                Vector3 desiredAngVel = axis.normalized * Mathf.Clamp(angleDeg * Mathf.Deg2Rad * kp, -0.35f, 0.35f);
                torque = (desiredAngVel - rb.angularVelocity) * kd;
            }
            else
            {
                torque = -rb.angularVelocity * kd;
            }

            rb.AddTorque(torque, ForceMode.Acceleration);

            // Sync with CSM DAP indicator if CSM
            var rocket = ApolloProLogicHandler.Instance?.apolloRocket;
            if (rocket != null && State.ActiveSpacecraft == AvailableSpacecrafts.CommandModule)
            {
                try { rocket.SetAttHoldDirectionToDir(desiredForward); } catch { }
            }

            return AttitudeErrorDeg;
        }

        private static void RunKillRotation()
        {
            var rb = GetActiveRigidbody();
            if (rb == null) return;

            rb.AddTorque(-rb.angularVelocity * 3.5f, ForceMode.Acceleration);
            float rateDeg = rb.angularVelocity.magnitude * Mathf.Rad2Deg;
            AttitudeErrorDeg = rateDeg;
            PhaseDetail = rateDeg < 0.08f
                ? "Attitude rates nulled (0.0°/s) — holding steady."
                : $"Damping rotation rates ({rateDeg:0.2}°/s)...";
        }

        private static void RunOrbitalAttitudeHold(AutopilotMode m)
        {
            var rb = GetActiveRigidbody();
            if (rb == null || !GetOrbitalBasis(out Vector3 pro, out Vector3 radOut, out Vector3 norm))
            {
                PhaseDetail = "Waiting for orbital velocity vector...";
                return;
            }

            Vector3 targetDir = m switch
            {
                AutopilotMode.PROGRADE => pro,
                AutopilotMode.RETROGRADE => -pro,
                AutopilotMode.NORMAL => norm,
                AutopilotMode.ANTI_NORMAL => -norm,
                AutopilotMode.RADIAL_IN => -radOut,
                AutopilotMode.RADIAL_OUT => radOut,
                _ => pro
            };

            Vector3 upHint = (m == AutopilotMode.RADIAL_IN || m == AutopilotMode.RADIAL_OUT) ? pro : radOut;
            float err = SlewToward(rb, targetDir, upHint);

            PhaseDetail = err < 1.2f
                ? $"Locked {m} (Error {err:0.2}°)"
                : $"Slewing to {m} ({err:0.1}° remaining)...";
        }

        // -------------------------------------------------------------
        // Autonomous Burn Execution (SPS / DPS / RCS)
        // -------------------------------------------------------------
        private static void RunAutoBurn()
        {
            var plan = BurnPlannerBridge.CurrentPlan;
            var rb = GetActiveRigidbody();
            if (!plan.Valid || rb == null)
            {
                PhaseDetail = "Burn aborted — missing plan or spacecraft.";
                Mode = AutopilotMode.OFF;
                return;
            }

            if (!GetOrbitalBasis(out Vector3 pro, out Vector3 radOut, out Vector3 norm))
            {
                PhaseDetail = "Waiting for orbital reference frame...";
                return;
            }

            Vector3 burnDir = plan.Attitude switch
            {
                "Retrograde" => -pro,
                "RadialIn" => -radOut,
                "RadialOut" => radOut,
                _ => pro
            };
            if (plan.BurnDirection.sqrMagnitude > 0.1f && plan.Attitude == "Custom")
                burnDir = plan.BurnDirection.normalized;

            float err = SlewToward(rb, burnDir, radOut, 3.0f, 3.6f);

            // Countdown to TIG
            if (!_burnActive)
            {
                if (BurnTigRemainingSec > 0f)
                {
                    BurnTigRemainingSec = Mathf.Max(0f, BurnTigRemainingSec - Time.fixedDeltaTime);
                }

                if (BurnTigRemainingSec > 10f)
                {
                    PhaseDetail = $"Slewing & Holding {plan.Attitude} (Err {err:0.1}°) | TIG T-{BurnTigRemainingSec:0.0}s";
                    return;
                }

                // Wait for alignment before igniting
                if (err > 4.0f)
                {
                    PhaseDetail = $"Final alignment before ignition (Err {err:0.1}°)...";
                    return;
                }

                // Ullage window (T-10s to T-0s)
                if (BurnTigRemainingSec > 0f)
                {
                    if (!_burnUllageStarted)
                    {
                        _burnUllageStarted = true;
                    }
                    // Gentle ullage settling thrust
                    ApplyCraftDeltaV(burnDir, 0.15f * Time.fixedDeltaTime);
                    PhaseDetail = $"ULLAGE SETTLING | TIG T-{BurnTigRemainingSec:0.1}s | Err {err:0.1}°";
                    return;
                }

                // IGNITION!
                _burnActive = true;
                _burnStartTime = Time.time;
                IgniteBurnEngine(plan.Engine);
            }

            // Active Burn: integrate ΔV smoothly toward zero!
            bool isRcs = plan.Engine.Contains("Translation");
            float accelFps2 = isRcs ? 0.8f : (BurnDvRemainingFps < 15f ? 3.5f : 10.5f);
            float dvStepFps = Mathf.Min(BurnDvRemainingFps, accelFps2 * Time.fixedDeltaTime);

            // Apply calibrated thrust through GravityEngine / craft propulsion
            ApplyCraftDeltaV(burnDir, dvStepFps);
            BurnDvRemainingFps -= dvStepFps;

            PhaseDetail = $"🔥 BURNING {plan.Engine} | ΔV Rem: {BurnDvRemainingFps:0.1} ft/s | Err {err:0.1}°";

            if (BurnDvRemainingFps <= 0.08f)
            {
                SafeCutoffEngines();
                _burnActive = false;
                BurnDvRemainingFps = 0f;
                PhaseDetail = $"✓ Burn Complete: {plan.Name}! Holding attitude.";
                Mode = AutopilotMode.KILL_ROT;
            }
        }

        private static void IgniteBurnEngine(string engineName)
        {
            try
            {
                if (engineName.Contains("LM_DPS") && State.Current?.LunarModuleState?.DPS != null)
                {
                    var dps = State.Current.LunarModuleState.DPS;
                    dps.ArmEngine();
                    dps.SetAutomaticThrust(1.0f);
                    dps.Ignite();
                }
            }
            catch { }
        }

        private static void SafeCutoffEngines()
        {
            try
            {
                var lm = State.Current?.LunarModuleState;
                if (lm?.DPS != null && lm.DPS.IsThrusting())
                {
                    lm.DPS.SetAutomaticThrust(0f);
                    lm.DPS.Cutoff();
                }
                if (lm?.APS != null && lm.APS.IsThrusting())
                {
                    lm.APS.Cutoff();
                }
            }
            catch { }
        }

        private static void ApplyCraftDeltaV(Vector3 worldDir, float dvFps)
        {
            if (dvFps <= 0f || worldDir.sqrMagnitude < 1e-5f) return;
            worldDir.Normalize();

            var logic = ApolloProLogicHandler.Instance;
            if (logic == null) return;

            // Convert ft/s -> m/s -> Force (Newtons) for ApplyTranslationalThrust or GravityEngine impulse
            float dvMs = dvFps * 0.3048f;
            float dt = Mathf.Max(0.005f, Time.fixedDeltaTime);

            if (State.ActiveSpacecraft == AvailableSpacecrafts.CommandModule && logic.apolloRocket != null && _applyTransThrust != null)
            {
                float massKg = Mathf.Max(5000f, logic.apolloRocket.GetMassInKg());
                float forceN = (massKg * dvMs) / dt;
                _applyTransThrust(logic.apolloRocket, forceN, worldDir);
                return;
            }

            // Fallback / LM GravityEngine impulse
            try
            {
                var ge = GravityEngine.Instance();
                var lm = _logicLmRef?.Invoke(logic);
                NBody? body = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule
                    ? lm?.GetComponent<NBody>()
                    : (logic.apolloRocket != null && _csmBodyRef != null ? _csmBodyRef(logic.apolloRocket) : null);

                if (ge != null && body != null)
                {
                    // Convert m/s -> km/s -> GravityEngine physics units
                    float dvKmS = dvMs / 1000f;
                    float scale = UniverseProperties.UnitsToKm > 0 ? UniverseProperties.UnitsToKm : 1f;
                    ge.ApplyImpulse(body, worldDir * (dvKmS * scale));
                }
            }
            catch { }
        }

        // -------------------------------------------------------------
        // Autonomous Docking (CSM <-> LM)
        // -------------------------------------------------------------
        private static void RunAutoDock()
        {
            var logic = ApolloProLogicHandler.Instance;
            var rocket = logic?.apolloRocket;
            var lm = logic != null && _logicLmRef != null ? _logicLmRef(logic) : null;
            var rb = GetActiveRigidbody();

            if (rocket == null || lm == null || rb == null)
            {
                PhaseDetail = "Docking unavailable — CSM or LM not both active in scene.";
                return;
            }

            Transform? csmPort = _csmProbeRef != null ? _csmProbeRef(rocket) : rocket.transform;
            Transform? lmPort = _lmConeRef != null ? _lmConeRef(lm) : lm.transform;
            if (csmPort == null || lmPort == null)
            {
                PhaseDetail = "Docking port transforms not found.";
                return;
            }

            bool flyingLM = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule;
            Transform myPort = flyingLM ? lmPort : csmPort;
            Transform targetPort = flyingLM ? csmPort : lmPort;

            Vector3 toTarget = targetPort.position - myPort.position;
            float dist = toTarget.magnitude;
            DockDistanceMeters = dist;

            // Check capture distance!
            float gameDockDist = _checkDockDist != null ? _checkDockDist(lm) : dist;
            if (dist < 1.35f || (gameDockDist > 0f && gameDockDist < 1.0f))
            {
                try
                {
                    if (_latchToDock == null || !_latchToDock(lm))
                    {
                        lm.DockNow(true);
                    }
                    PhaseDetail = "✓ HARD DOCK ACHIEVED! Capture latches engaged.";
                    Mode = AutopilotMode.KILL_ROT;
                    return;
                }
                catch { }
            }

            // Phase 1: Point docking port straight at target port (anti-parallel to target docking axis)
            Vector3 desiredDir = dist > 4f ? toTarget.normalized : (-targetPort.forward).normalized;
            float err = SlewToward(rb, desiredDir, targetPort.up, 2.8f, 3.6f);

            // Compute relative velocity if GravityEngine bodies exist
            Vector3 relVel = Vector3.zero;
            try
            {
                var ge = GravityEngine.Instance();
                var csmBody = _csmBodyRef != null ? _csmBodyRef(rocket) : null;
                var lmBody = lm.GetComponent<NBody>();
                if (ge != null && csmBody != null && lmBody != null)
                {
                    Vector3 vCsm = ge.GetVelocity(csmBody);
                    Vector3 vLm = ge.GetVelocity(lmBody);
                    relVel = flyingLM ? (vLm - vCsm) : (vCsm - vLm);
                }
            }
            catch { }

            if (err > 6.0f)
            {
                PhaseDetail = $"AUTO-DOCK Phase 1: Aligning docking axis ({err:0.1}° err, Range {dist:0.1} m)";
                return;
            }

            // Phase 2 & 3: Controlled corridor approach along docking vector
            float desiredCloseSpeedMs = dist > 30f ? 1.5f : (dist > 10f ? 0.8f : (dist > 3f ? 0.35f : 0.18f));
            Vector3 desiredRelVel = toTarget.normalized * desiredCloseSpeedMs;
            Vector3 velError = desiredRelVel - relVel;

            float correctionMagFps = Mathf.Clamp(velError.magnitude * 3.28084f, 0.05f, 1.8f);
            if (velError.sqrMagnitude > 0.0004f)
            {
                ApplyCraftDeltaV(velError.normalized, correctionMagFps * Time.fixedDeltaTime);
            }

            PhaseDetail = dist > 5f
                ? $"AUTO-DOCK Phase 2: Closing on corridor ({dist:0.1} m @ {desiredCloseSpeedMs:0.2} m/s | Err {err:0.1}°)"
                : $"AUTO-DOCK Phase 3: Final Soft-Dock Approach ({dist:0.2} m | Err {err:0.1}°)";
        }

        // -------------------------------------------------------------
        // Autonomous Lunar Powered Descent & Touchdown (LM)
        // -------------------------------------------------------------
        private static void RunAutoLunarLanding()
        {
            var logic = ApolloProLogicHandler.Instance;
            var lmState = State.Current?.LunarModuleState;
            var lmCtrl = logic != null && _logicLmRef != null ? _logicLmRef(logic) : null;
            var landCtrl = logic != null && _logicLmLandRef != null ? _logicLmLandRef(logic) : null;

            if (lmState == null || lmCtrl == null || lmState.DPS == null)
            {
                PhaseDetail = "AUTO-LAND requires active Lunar Module with Descent Propulsion System.";
                Mode = AutopilotMode.OFF;
                return;
            }

            var dps = lmState.DPS;
            double altFt = lmState.AltitudeMeters * 3.28084;
            double vSpeedFps = lmState.AltitudeChangeFeetSec;
            bool inSurfacePhysics = landCtrl != null && landCtrl.gameObject.activeInHierarchy && _lmLandRbRef != null && _lmLandRbRef(landCtrl) != null;

            // Deploy landing gear automatically below 1,500 ft!
            if (altFt < 1500 && !_gearDeployed)
            {
                _gearDeployed = true;
                try { lmCtrl.DeployLandingGear(); } catch { }
            }

            // Touchdown detection!
            if (inSurfacePhysics && altFt < 10.0 && Math.Abs(vSpeedFps) < 4.5)
            {
                dps.SetAutomaticThrust(0f);
                dps.Cutoff();
                PhaseDetail = $"✓ CONTACT LIGHT! Lunar Touchdown at {BurnPlannerBridge.SelectedLandingSiteName} (V/S {vSpeedFps:0.1} ft/s)";
                Mode = AutopilotMode.OFF;
                return;
            }

            // Arm & ignite DPS
            if (!dps.IsArmed()) dps.ArmEngine();
            if (!dps.IsThrusting()) dps.Ignite();

            // CASE A: Orbital PDI / Braking Phase (high altitude)
            if (!inSurfacePhysics || altFt > 50000)
            {
                var rb = GetActiveRigidbody();
                if (rb != null && GetOrbitalBasis(out Vector3 pro, out Vector3 radOut, out _))
                {
                    // Pitch retrograde slightly nose-up to kill orbital velocity and descend toward site
                    Vector3 brakeDir = (-pro + radOut * 0.18f).normalized;
                    float err = SlewToward(rb, brakeDir, radOut, 2.8f, 3.6f);
                    dps.SetAutomaticThrust(err < 25f ? 1.0f : 0.15f);

                    // Assist retrograde deceleration smoothly so PDI completes reliably
                    if (err < 20f)
                    {
                        ApplyCraftDeltaV(-pro, 8.5f * Time.fixedDeltaTime);
                    }

                    PhaseDetail = $"AUTO-LAND Phase 1 (PDI Braking): Alt {altFt:0} ft | V/S {vSpeedFps:0} ft/s | Site {lmState.DistanceToLandingPointInNM:0.1} NM";
                }
                return;
            }

            // CASE B: Lunar Surface Physics (Approach -> Hover -> Soft Touchdown!)
            var landRb = _lmLandRbRef!(landCtrl!);
            Vector3 upDir = Vector3.up;
            Vector3 vel = landRb.linearVelocity;
            Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);

            // Steer toward landingTargetFlag if present & above 150 ft
            Vector3 desiredHorizVel = Vector3.zero;
            GameObject? flag = _lmFlagRef != null ? _lmFlagRef(landCtrl!) : null;
            if (flag != null && altFt > 120)
            {
                Vector3 toFlag = flag.transform.position - landRb.position;
                toFlag.y = 0f;
                float flagDist = toFlag.magnitude;
                if (flagDist > 5f)
                {
                    float approachSpeed = Mathf.Clamp(flagDist * 0.18f, 0f, 35f);
                    desiredHorizVel = toFlag.normalized * approachSpeed;
                }
            }

            // Tilt slightly to null horizontal velocity error (max 22° tilt, max 6° under 120 ft)
            Vector3 horizErr = desiredHorizVel - horizVel;
            float maxTilt = altFt < 120 ? 0.10f : 0.38f;
            Vector3 tiltVec = Vector3.ClampMagnitude(horizErr * 0.04f, maxTilt);
            Vector3 desiredThrustUp = (upDir + tiltVec).normalized;

            // LM thrust axis is +Y (up) in Lunar Stay frame -> align transform.up to desiredThrustUp
            Quaternion targetRot = Quaternion.FromToRotation(landRb.transform.up, desiredThrustUp) * landRb.rotation;
            Quaternion deltaRot = targetRot * Quaternion.Inverse(landRb.rotation);
            deltaRot.ToAngleAxis(out float angDeg, out Vector3 axis);
            if (angDeg > 180f) angDeg -= 360f;
            AttitudeErrorDeg = Mathf.Abs(angDeg);
            if (Mathf.Abs(angDeg) > 0.05f && axis.sqrMagnitude > 1e-5f)
            {
                Vector3 desiredAngVel = axis.normalized * Mathf.Clamp(angDeg * Mathf.Deg2Rad * 3.0f, -0.45f, 0.45f);
                landRb.AddTorque((desiredAngVel - landRb.angularVelocity) * 4.0f, ForceMode.Acceleration);
            }
            else
            {
                landRb.AddTorque(-landRb.angularVelocity * 4.0f, ForceMode.Acceleration);
            }

            // Smooth Apollo descent-rate schedule (ft/s -> m/s)
            float targetDescentFps;
            if (altFt > 3000) targetDescentFps = -Mathf.Clamp((float)Math.Sqrt(altFt) * 1.45f, 35f, 220f);
            else if (altFt > 500) targetDescentFps = -Mathf.Clamp((float)(altFt / 45.0), 12f, 55f);
            else if (altFt > 100) targetDescentFps = -Mathf.Clamp((float)(altFt / 35.0), 4.5f, 12f);
            else targetDescentFps = -2.5f; // Silky smooth touchdown rate!

            float vzErrorFps = targetDescentFps - (float)vSpeedFps;
            // Hover throttle on Moon (~1.62 m/s² gravity) is ~0.25-0.38 depending on fuel mass
            float throttle = Mathf.Clamp(0.32f + vzErrorFps * 0.045f, 0.10f, 1.0f);
            dps.SetAutomaticThrust(throttle);

            string subPhase = altFt > 500 ? "Phase 2 (Powered Approach)" : "Phase 3 (Terminal Hover & Touchdown)";
            PhaseDetail = $"AUTO-LAND {subPhase}: Alt {altFt:0} ft | V/S {vSpeedFps:0.1} (Tgt {targetDescentFps:0.1}) ft/s | Thr {throttle * 100f:0}%";
        }

        // -------------------------------------------------------------
        // Autonomous Lunar Liftoff & Orbit Insertion (LM Ascent Stage)
        // -------------------------------------------------------------
        private static void RunAutoLunarAscent()
        {
            var logic = ApolloProLogicHandler.Instance;
            var lmState = State.Current?.LunarModuleState;
            var lmCtrl = logic != null && _logicLmRef != null ? _logicLmRef(logic) : null;
            var landCtrl = logic != null && _logicLmLandRef != null ? _logicLmLandRef(logic) : null;

            if (lmState == null || lmCtrl == null || lmState.APS == null)
            {
                PhaseDetail = "AUTO-ASCENT requires active Lunar Module with Ascent Propulsion System.";
                Mode = AutopilotMode.OFF;
                return;
            }

            var aps = lmState.APS;
            double altFt = lmState.AltitudeMeters * 3.28084;
            float curApNm = lmState.Orbit != null ? lmState.Orbit.ApogeeAltitude / 1.852f : 0f;

            // Step 1: Fire staging pyros & ignite APS!
            if (!_ascentStaged)
            {
                _ascentStaged = true;
                try
                {
                    if (lmState.Stage == LunarModuleState.LMState.Descent)
                    {
                        lmCtrl.Stage();
                    }
                    aps.OpenHeValves();
                    aps.ArmEngine();
                    aps.Ignite();
                }
                catch { }
            }
            else if (!aps.IsThrusting())
            {
                try
                {
                    aps.ArmEngine();
                    aps.Ignite();
                }
                catch { }
            }

            // Step 2: Check orbital insertion cutoff!
            if (curApNm >= TargetAscentOrbitNm && altFt > 45000)
            {
                aps.Cutoff();
                PhaseDetail = $"✓ LUNAR ORBIT INSERTION ACHIEVED! Ap {curApNm:0.1} NM — Circularizing next...";
                // Auto-plan circularization burn at Apolune!
                BurnPlannerBridge.PlanOrbitBurn("circ_ap", TargetAscentOrbitNm, TargetAscentOrbitNm, 180f, 0f, "Prograde", "LM_Translation");
                Mode = AutopilotMode.PROGRADE;
                return;
            }

            // Step 3: Gravity-turn pitch guidance (Vertical 0-350 ft -> smooth pitchover East toward target orbit)
            float pitchFromVerticalDeg;
            if (altFt < 350) pitchFromVerticalDeg = 0f;
            else if (altFt < 5000) pitchFromVerticalDeg = Mathf.Lerp(0f, 45f, (float)((altFt - 350) / 4650.0));
            else if (altFt < 25000) pitchFromVerticalDeg = Mathf.Lerp(45f, 75f, (float)((altFt - 5000) / 20000.0));
            else pitchFromVerticalDeg = 86f;

            var rb = GetActiveRigidbody();
            if (rb != null)
            {
                bool inSurfacePhysics = landCtrl != null && landCtrl.gameObject.activeInHierarchy;
                if (inSurfacePhysics)
                {
                    Vector3 desiredUp = Quaternion.AngleAxis(pitchFromVerticalDeg, Vector3.forward) * Vector3.up;
                    Quaternion targetRot = Quaternion.FromToRotation(rb.transform.up, desiredUp) * rb.rotation;
                    Quaternion deltaRot = targetRot * Quaternion.Inverse(rb.rotation);
                    deltaRot.ToAngleAxis(out float angDeg, out Vector3 axis);
                    if (angDeg > 180f) angDeg -= 360f;
                    AttitudeErrorDeg = Mathf.Abs(angDeg);
                    if (Mathf.Abs(angDeg) > 0.05f && axis.sqrMagnitude > 1e-5f)
                    {
                        Vector3 desiredAngVel = axis.normalized * Mathf.Clamp(angDeg * Mathf.Deg2Rad * 3.0f, -0.45f, 0.45f);
                        rb.AddTorque((desiredAngVel - rb.angularVelocity) * 4.0f, ForceMode.Acceleration);
                    }
                }
                else if (GetOrbitalBasis(out Vector3 pro, out Vector3 radOut, out _))
                {
                    Vector3 ascentDir = Vector3.Slerp(radOut, pro, pitchFromVerticalDeg / 90f).normalized;
                    SlewToward(rb, ascentDir, radOut, 2.8f, 3.5f);
                }
            }

            PhaseDetail = $"🚀 AUTO-ASCENT: Alt {altFt:0} ft | Pitch {90f - pitchFromVerticalDeg:0}° | Ap {curApNm:0.1} / {TargetAscentOrbitNm:0} NM";
        }

        public static string BuildAutopilotJson()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"autopilot\",\"data\":{");
            sb.Append($"\"mode\":\"{Mode}\",");
            sb.Append($"\"detail\":\"{JsonHelper.Escape(PhaseDetail)}\",");
            sb.Append($"\"attErr\":{AttitudeErrorDeg.ToString("0.1", inv)},");
            sb.Append($"\"dvRem\":{BurnDvRemainingFps.ToString("0.1", inv)},");
            sb.Append($"\"tigRem\":{BurnTigRemainingSec.ToString("0.1", inv)},");
            sb.Append($"\"dockDist\":{DockDistanceMeters.ToString("0.2", inv)},");
            sb.Append($"\"targetOrbitNm\":{TargetAscentOrbitNm.ToString("0", inv)}");
            sb.Append("}}");
            return sb.ToString();
        }
    }
}
