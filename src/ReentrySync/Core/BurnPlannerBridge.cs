using System;
using System.Globalization;
using System.Text;
using Apollo;
using Apollo.Maneuvers;
using HarmonyLib;
using UnityEngine;

namespace ReentrySync.Core
{
    public class PlannedBurnSummary
    {
        public bool Valid { get; set; }
        public string Name { get; set; } = "No Burn Planned";
        public string Body { get; set; } = "MOON";
        public string Engine { get; set; } = "CSM_SPS";
        public string Attitude { get; set; } = "Prograde";
        public float PhaseAngleDeg { get; set; }
        public float DeltaVFps { get; set; }
        public float BurnLengthSec { get; set; }
        public float TimeToIgnitionSec { get; set; }
        public float PredictedApNm { get; set; }
        public float PredictedPeNm { get; set; }
        public string BurnPad { get; set; } = "";
        public Vector3 BurnDirection { get; set; } = Vector3.zero;
        public string StatusMessage { get; set; } = "Enter target orbit or select a preset.";
    }

    public static class BurnPlannerBridge
    {
        private const double MU_EARTH_KM3 = 398600.4418;
        private const double R_EARTH_KM = 6378.137;
        private const double MU_MOON_KM3 = 4902.800066;
        private const double R_MOON_KM = 1737.4;
        private const double NM_TO_KM = 1.852;
        private const double KMS_TO_FPS = 3280.839895;

        public static PlannedBurnSummary CurrentPlan { get; private set; } = new PlannedBurnSummary();
        public static string SelectedLandingSiteName { get; private set; } = "Default Mission Site";
        public static float SelectedLandingLat { get; private set; } = 0f;
        public static float SelectedLandingLon { get; private set; } = 0f;
        public static float LandingTrackOffsetDeg { get; private set; } = 0f;

        private static readonly AccessTools.FieldRef<ApolloProLogicHandler, float>? _landingPointMoverRef =
            AccessTools.FieldRefAccess<ApolloProLogicHandler, float>("landingPointMover");

        private static readonly AccessTools.FieldRef<ProApolloRocket, ManeuverPlanner>? _maneuverPlannerRef =
            AccessTools.FieldRefAccess<ProApolloRocket, ManeuverPlanner>("maneuverPlanner");

        private static readonly AccessTools.FieldRef<OrbitState, double>? _timeToApRef =
            AccessTools.FieldRefAccess<OrbitState, double>("TimeToAp");

        private static readonly AccessTools.FieldRef<OrbitState, double>? _periodRef =
            AccessTools.FieldRefAccess<OrbitState, double>("Period");

        private static double GetTimeToAp(OrbitState? orbit)
        {
            if (orbit == null) return 0;
            if (_timeToApRef != null) return _timeToApRef(orbit);
            double period = _periodRef != null ? _periodRef(orbit) : 0;
            return period > 0 ? (orbit.TimeToPe + period * 0.5) % period : 0;
        }

        public static ManeuverPlanner? GetManeuverPlanner()
        {
            var rocket = ApolloProLogicHandler.Instance?.apolloRocket;
            if (rocket != null && _maneuverPlannerRef != null)
            {
                var mp = _maneuverPlannerRef(rocket);
                if (mp != null) return mp;
            }
            return UnityEngine.Object.FindAnyObjectByType<ManeuverPlanner>();
        }

        public static bool IsInLunarSOI()
        {
            if (State.Current == null) return false;
            return State.Current.SOIDistToMoon > 0 && State.Current.SOIDistToMoon < State.Current.SOIDistToEarth;
        }

        public static OrbitState? GetActiveOrbit()
        {
            if (State.Current == null) return null;
            if (State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule && State.Current.LunarModuleState?.Orbit != null)
                return State.Current.LunarModuleState.Orbit;
            return State.Current.Orbit;
        }

        // -------------------------------------------------------------
        // Compute Custom Orbit Transfer / Circularization / DOI / LOI
        // -------------------------------------------------------------
        public static PlannedBurnSummary PlanOrbitBurn(
            string mode,
            double targetApNm,
            double targetPeNm,
            float customPhaseDeg,
            float customDvFps,
            string attitudeStr,
            string engineStr)
        {
            var plan = new PlannedBurnSummary();
            var orbit = GetActiveOrbit();
            if (orbit == null || State.Current == null)
            {
                plan.StatusMessage = "No active spacecraft orbit available.";
                CurrentPlan = plan;
                return plan;
            }

            bool lunar = IsInLunarSOI();
            double mu = lunar ? MU_MOON_KM3 : MU_EARTH_KM3;
            double radiusKm = lunar ? R_MOON_KM : R_EARTH_KM;
            plan.Body = lunar ? "MOON" : "EARTH";

            // Current orbit altitudes in km (Reentry OrbitState.ApogeeAltitude / PerigeeAltitude are in Kilometers!)
            double curApKm = Math.Max(0, orbit.ApogeeAltitude);
            double curPeKm = Math.Max(0, orbit.PerigeeAltitude);
            double curApNm = curApKm / NM_TO_KM;
            double curPeNm = curPeKm / NM_TO_KM;
            double rAp = radiusKm + curApKm;
            double rPe = radiusKm + curPeKm;
            double aCur = 0.5 * (rAp + rPe);

            double dvKms = 0;
            float phaseDeg = customPhaseDeg;
            var att = ManeuverPlanner.BurnAttitudeOptions.Prograde;

            switch (mode.ToLowerInvariant())
            {
                case "circ_pe":
                case "loi_circ":
                {
                    // Burn at Periapsis (phase 0°) to circularize at current Pe (or targetPeNm if > 0)
                    double vPeCur = Math.Sqrt(mu * (2.0 / rPe - 1.0 / aCur));
                    double rTarget = (targetPeNm > 1) ? radiusKm + targetPeNm * NM_TO_KM : rPe;
                    double aNew = 0.5 * (rPe + rTarget);
                    double vPeNew = Math.Sqrt(mu * (2.0 / rPe - 1.0 / aNew));
                    dvKms = vPeNew - vPeCur;
                    phaseDeg = 0f;
                    att = dvKms >= 0 ? ManeuverPlanner.BurnAttitudeOptions.Prograde : ManeuverPlanner.BurnAttitudeOptions.Retrograde;
                    plan.Name = lunar ? "LOI / Circularize at Perilune" : "Circularize at Perigee";
                    break;
                }

                case "circ_ap":
                {
                    // Burn at Apoapsis (phase 180°) to circularize at current Ap
                    double vApCur = Math.Sqrt(mu * (2.0 / rAp - 1.0 / aCur));
                    double vApCirc = Math.Sqrt(mu / rAp);
                    dvKms = vApCirc - vApCur;
                    phaseDeg = 180f;
                    att = dvKms >= 0 ? ManeuverPlanner.BurnAttitudeOptions.Prograde : ManeuverPlanner.BurnAttitudeOptions.Retrograde;
                    plan.Name = lunar ? "Circularize at Apolune" : "Circularize at Apogee";
                    break;
                }

                case "doi_60x8":
                {
                    // Descent Orbit Insertion: burn retrograde at Apoapsis (or opposite landing site) to lower Perilune to 8.5 NM
                    double rBurn = rAp;
                    double vCur = Math.Sqrt(mu * (2.0 / rBurn - 1.0 / aCur));
                    double rTargetPe = radiusKm + 8.5 * NM_TO_KM;
                    double aNew = 0.5 * (rBurn + rTargetPe);
                    double vNew = Math.Sqrt(mu * (2.0 / rBurn - 1.0 / aNew));
                    dvKms = vNew - vCur;
                    phaseDeg = customPhaseDeg > 0 ? customPhaseDeg : 180f;
                    att = ManeuverPlanner.BurnAttitudeOptions.Retrograde;
                    plan.Name = "DOI — Descent Orbit Insertion (Perilune 8.5 NM)";
                    break;
                }

                case "custom_orbit":
                {
                    // Transfer from current orbit toward targetApNm x targetPeNm
                    // Decide whether to burn at Pe (adjusting Ap) or at Ap (adjusting Pe)
                    double errAp = Math.Abs(curApNm - targetApNm);
                    double errPe = Math.Abs(curPeNm - targetPeNm);

                    if (errAp >= errPe)
                    {
                        // Burn at Periapsis to set Apoapsis = targetApNm
                        double vCur = Math.Sqrt(mu * (2.0 / rPe - 1.0 / aCur));
                        double rNewAp = radiusKm + Math.Max(5.0, targetApNm) * NM_TO_KM;
                        double aNew = 0.5 * (rPe + rNewAp);
                        double vNew = Math.Sqrt(mu * (2.0 / rPe - 1.0 / aNew));
                        dvKms = vNew - vCur;
                        phaseDeg = 0f;
                        plan.Name = $"Adjust Ap → {targetApNm:0.0} NM (at Pe)";
                    }
                    else
                    {
                        // Burn at Apoapsis to set Periapsis = targetPeNm
                        double vCur = Math.Sqrt(mu * (2.0 / rAp - 1.0 / aCur));
                        double rNewPe = radiusKm + Math.Max(5.0, targetPeNm) * NM_TO_KM;
                        double aNew = 0.5 * (rAp + rNewPe);
                        double vNew = Math.Sqrt(mu * (2.0 / rAp - 1.0 / aNew));
                        dvKms = vNew - vCur;
                        phaseDeg = 180f;
                        plan.Name = $"Adjust Pe → {targetPeNm:0.0} NM (at Ap)";
                    }
                    att = dvKms >= 0 ? ManeuverPlanner.BurnAttitudeOptions.Prograde : ManeuverPlanner.BurnAttitudeOptions.Retrograde;
                    break;
                }

                default:
                {
                    // Raw manual / MCC burn
                    dvKms = customDvFps / KMS_TO_FPS;
                    phaseDeg = customPhaseDeg;
                    if (Enum.TryParse(attitudeStr, true, out ManeuverPlanner.BurnAttitudeOptions parsedAtt))
                        att = parsedAtt;
                    plan.Name = $"Custom Maneuver ({att}, {Math.Abs(customDvFps):0.0} ft/s)";
                    break;
                }
            }

            float finalDvFps = (float)Math.Abs(dvKms * KMS_TO_FPS);
            if (mode.Equals("raw", StringComparison.OrdinalIgnoreCase))
            {
                finalDvFps = Math.Abs(customDvFps);
            }

            // Pick engine
            var engine = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule
                ? ManeuverPlanner.EngineOptions.LM_DPS
                : ManeuverPlanner.EngineOptions.CSM_SPS;

            if (!string.IsNullOrEmpty(engineStr) && Enum.TryParse(engineStr, true, out ManeuverPlanner.EngineOptions parsedEng))
            {
                engine = parsedEng;
            }
            else if (finalDvFps < 12f)
            {
                // Small trim/MCC burns automatically recommend RCS Translation!
                engine = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule
                    ? ManeuverPlanner.EngineOptions.LM_Translation
                    : ManeuverPlanner.EngineOptions.CSM_Translation;
            }

            plan.PhaseAngleDeg = ((phaseDeg % 360f) + 360f) % 360f;
            plan.DeltaVFps = finalDvFps;
            plan.Attitude = att.ToString();
            plan.Engine = engine.ToString();
            plan.Valid = finalDvFps > 0.1f;

            // Estimate predicted orbit analytically immediately
            ComputeAnalyticalPrediction(orbit, radiusKm, mu, plan, att, dvKms);

            // Also feed into Reentry's native GravityEngine ManeuverPlanner!
            var mp = GetManeuverPlanner();
            if (mp != null)
            {
                try
                {
                    ManeuverPlanner.RemoteBurnData.burnPhase = plan.PhaseAngleDeg;
                    ManeuverPlanner.RemoteBurnData.dV = plan.DeltaVFps;
                    ManeuverPlanner.RemoteBurnData.burnAttitude = att;
                    ManeuverPlanner.RemoteBurnData.burnEngine = engine;
                    mp.EstablishRemotePlannerConnection();
                    mp.RemoteRefreshBurnCalulcation();

                    var res = ManeuverPlanner.RemoteBurnCalculationResults;
                    if (res.targetAp > 0 || res.targetPe > 0 || res.burnLength > 0)
                    {
                        plan.TimeToIgnitionSec = res.timeToIgnition;
                        plan.BurnLengthSec = res.burnLength;
                        if (res.targetAp > 0) plan.PredictedApNm = res.targetAp;
                        if (res.targetPe > 0) plan.PredictedPeNm = res.targetPe;
                        plan.BurnPad = res.BurnPAD ?? "";
                        plan.BurnDirection = res.burnDirection;
                    }
                    plan.StatusMessage = $"Planned via GravityEngine: ΔV {plan.DeltaVFps:0.1} ft/s ({plan.Attitude}) using {plan.Engine}";
                }
                catch (Exception ex)
                {
                    plan.StatusMessage = $"Analytical burn planned (ΔV {plan.DeltaVFps:0.1} ft/s). Native planner note: {ex.Message}";
                }
            }
            else
            {
                plan.StatusMessage = $"Analytical burn planned: ΔV {plan.DeltaVFps:0.1} ft/s ({plan.Attitude})";
            }

            CurrentPlan = plan;
            return plan;
        }

        private static void ComputeAnalyticalPrediction(OrbitState orbit, double radiusKm, double mu, PlannedBurnSummary plan, ManeuverPlanner.BurnAttitudeOptions att, double signedDvKms)
        {
            double rAp = radiusKm + Math.Max(0, orbit.ApogeeAltitude);
            double rPe = radiusKm + Math.Max(0, orbit.PerigeeAltitude);
            double aCur = 0.5 * (rAp + rPe);

            bool atPe = Math.Abs(plan.PhaseAngleDeg) < 45f || Math.Abs(plan.PhaseAngleDeg - 360f) < 45f;
            double rBurn = atPe ? rPe : rAp;
            double vCur = Math.Sqrt(Math.Max(0.0001, mu * (2.0 / rBurn - 1.0 / aCur)));

            double dv = plan.DeltaVFps / KMS_TO_FPS;
            if (att == ManeuverPlanner.BurnAttitudeOptions.Retrograde) dv = -dv;

            double vNew = Math.Max(0.05, vCur + dv);
            double invA = 2.0 / rBurn - (vNew * vNew) / mu;
            if (invA > 1e-6)
            {
                double aNew = 1.0 / invA;
                double rOther = Math.Max(radiusKm, 2.0 * aNew - rBurn);
                double newAp = (Math.Max(rBurn, rOther) - radiusKm) / NM_TO_KM;
                double newPe = (Math.Min(rBurn, rOther) - radiusKm) / NM_TO_KM;
                plan.PredictedApNm = (float)Math.Max(0, newAp);
                plan.PredictedPeNm = (float)Math.Max(0, newPe);
            }

            // Estimate TIG from TimeToPe / TimeToAp
            if (atPe)
                plan.TimeToIgnitionSec = Math.Max(15f, orbit.TimeToPe);
            else if (Math.Abs(plan.PhaseAngleDeg - 180f) < 45f)
                plan.TimeToIgnitionSec = Math.Max(15f, (float)GetTimeToAp(orbit));
            else
                plan.TimeToIgnitionSec = 60f;

            // Approximate burn duration (SPS ~91 kN / 28,000 kg -> ~3.2 m/s² = ~10.6 ft/s², DPS ~45 kN / 15,000 kg -> ~10 ft/s², RCS ~0.4 ft/s²)
            float accelFps2 = plan.Engine.Contains("Translation") ? 0.45f : 10.2f;
            plan.BurnLengthSec = plan.DeltaVFps / accelFps2;
        }

        public static bool UploadPlanToAGC(out string message)
        {
            message = "";
            var mp = GetManeuverPlanner();
            if (mp == null || !CurrentPlan.Valid)
            {
                message = "No valid burn plan to upload, or ManeuverPlanner unavailable.";
                return false;
            }

            try
            {
                mp.RequestBurn(true);
                message = $"Uploaded '{CurrentPlan.Name}' burn PAD to Guidance Computer!";
                CurrentPlan.StatusMessage = message;
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to upload burn PAD: {ex.Message}";
                return false;
            }
        }

        // -------------------------------------------------------------
        // Lunar Landing Site Selector
        // -------------------------------------------------------------
        public static bool SelectLandingSite(string siteName, float lat, float lon, float trackAngleDeg, bool useTrackAngle, out string message)
        {
            message = "";
            var logic = ApolloProLogicHandler.Instance;
            var lm = State.Current?.LunarModuleState;
            if (logic == null || lm == null)
            {
                message = "Lunar Module state not active in scene.";
                return false;
            }

            try
            {
                SelectedLandingSiteName = string.IsNullOrEmpty(siteName) ? $"Custom ({lat:0.2}°, {lon:0.2}°)" : siteName;
                SelectedLandingLat = lat;
                SelectedLandingLon = lon;

                if (useTrackAngle && _landingPointMoverRef != null)
                {
                    LandingTrackOffsetDeg = ((trackAngleDeg % 360f) + 360f) % 360f;
                    _landingPointMoverRef(logic) = LandingTrackOffsetDeg;
                }
                else if (_landingPointMoverRef != null)
                {
                    // Align orbital ground-track mover toward target longitude relative to current sub-lunar point
                    float curLon = lm.Orbit != null ? lm.Orbit.MoonLongitude : (State.Current?.Orbit?.MoonLongitude ?? 0f);
                    float deltaLon = lon - curLon;
                    LandingTrackOffsetDeg = ((deltaLon % 360f) + 360f) % 360f;
                    _landingPointMoverRef(logic) = LandingTrackOffsetDeg;
                }

                lm.SearchLandingPointLatitude = lat;
                lm.SearchLandingPointLongitude = lon;
                lm.LandingPointLatitude = lat;
                lm.LandingPointLongitude = lon;

                // Apply directly so Reentry rotates lunarLandingSiteREFSMMAT and aligns LM IMU + LGC p52framePreferred!
                logic.SetLandingpoint(true);

                message = $"Landing site locked: {SelectedLandingSiteName} ({lat:0.2}°N, {lon:0.2}°E) — LM IMU & LPD aligned!";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Error setting landing site: {ex.Message}";
                return false;
            }
        }

        public static string BuildPlannerJson()
        {
            var inv = CultureInfo.InvariantCulture;
            var lm = State.Current?.LunarModuleState;
            var orb = GetActiveOrbit();
            var sb = new StringBuilder();

            double curApNm = (orb?.ApogeeAltitude ?? 0f) / NM_TO_KM;
            double curPeNm = (orb?.PerigeeAltitude ?? 0f) / NM_TO_KM;
            sb.Append("{\"type\":\"planner\",\"data\":{");
            sb.Append($"\"soi\":\"{(IsInLunarSOI() ? "MOON" : "EARTH")}\",");
            sb.Append($"\"curAp\":{curApNm.ToString("0.1", inv)},");
            sb.Append($"\"curPe\":{curPeNm.ToString("0.1", inv)},");
            sb.Append($"\"curInc\":{(orb?.Inclination ?? 0f).ToString("0.2", inv)},");
            sb.Append($"\"timeToPe\":{(orb?.TimeToPe ?? 0f).ToString("0", inv)},");
            sb.Append($"\"timeToAp\":{GetTimeToAp(orb).ToString("0", inv)},");
            sb.Append($"\"moonLat\":{(orb?.MoonLatitude ?? 0f).ToString("0.2", inv)},");
            sb.Append($"\"moonLon\":{(orb?.MoonLongitude ?? 0f).ToString("0.2", inv)},");

            // Current burn plan
            sb.Append("\"plan\":{");
            sb.Append($"\"valid\":{(CurrentPlan.Valid ? "true" : "false")},");
            sb.Append($"\"name\":\"{JsonHelper.Escape(CurrentPlan.Name)}\",");
            sb.Append($"\"body\":\"{JsonHelper.Escape(CurrentPlan.Body)}\",");
            sb.Append($"\"engine\":\"{JsonHelper.Escape(CurrentPlan.Engine)}\",");
            sb.Append($"\"attitude\":\"{JsonHelper.Escape(CurrentPlan.Attitude)}\",");
            sb.Append($"\"phase\":{CurrentPlan.PhaseAngleDeg.ToString("0.1", inv)},");
            sb.Append($"\"dv\":{CurrentPlan.DeltaVFps.ToString("0.1", inv)},");
            sb.Append($"\"burnLen\":{CurrentPlan.BurnLengthSec.ToString("0.1", inv)},");
            sb.Append($"\"tig\":{CurrentPlan.TimeToIgnitionSec.ToString("0.0", inv)},");
            sb.Append($"\"predAp\":{CurrentPlan.PredictedApNm.ToString("0.1", inv)},");
            sb.Append($"\"predPe\":{CurrentPlan.PredictedPeNm.ToString("0.1", inv)},");
            sb.Append($"\"status\":\"{JsonHelper.Escape(CurrentPlan.StatusMessage)}\",");
            sb.Append($"\"pad\":\"{JsonHelper.Escape(CurrentPlan.BurnPad)}\"");
            sb.Append("},");

            // Landing Site info
            float siteLat = lm != null ? lm.LandingPointLatitude : SelectedLandingLat;
            float siteLon = lm != null ? lm.LandingPointLongitude : SelectedLandingLon;
            float distNm = lm != null ? lm.DistanceToLandingPointInNM : 0f;
            float burnDistNm = lm != null ? lm.BurnDistanceInNM : 0f;

            sb.Append("\"landing\":{");
            sb.Append($"\"name\":\"{JsonHelper.Escape(SelectedLandingSiteName)}\",");
            sb.Append($"\"lat\":{siteLat.ToString("0.2", inv)},");
            sb.Append($"\"lon\":{siteLon.ToString("0.2", inv)},");
            sb.Append($"\"trackDeg\":{LandingTrackOffsetDeg.ToString("0.1", inv)},");
            sb.Append($"\"distNm\":{distNm.ToString("0.1", inv)},");
            sb.Append($"\"burnDistNm\":{burnDistNm.ToString("0.1", inv)}");
            sb.Append("}");

            sb.Append("}}");
            return sb.ToString();
        }
    }
}
