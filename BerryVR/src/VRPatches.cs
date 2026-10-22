using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BerryVR
{
    /// <summary>
    /// Small gameplay patches that turn the first-person tools into tracked
    /// motion-controlled tools. Every patch is passive while OpenVR is inactive.
    /// </summary>
    public static class VRPatches
    {
        private static readonly FieldInfo HeldRb =
            typeof(PlayerGrabber).GetField("heldRigidBody", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HeldPick =
            typeof(PlayerGrabber).GetField("heldPickUppable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HoldSpot =
            typeof(PlayerGrabber).GetField("itemHoldSpot", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HoldForce =
            typeof(PlayerGrabber).GetField("holdForcePower", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TorqueThreshold =
            typeof(PlayerGrabber).GetField("torqueThreshold", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TorqueFacing =
            typeof(PlayerGrabber).GetField("heldObjectTorqueFacingPower", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GrabberCam =
            typeof(PlayerGrabber).GetField("playerCam", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo CrosshairBasic =
            typeof(HudManager).GetField("crosshair_Basic", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CrosshairUsable =
            typeof(HudManager).GetField("crosshair_Usable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CrosshairMouse =
            typeof(HudManager).GetField("crosshair_MouseCursor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool VRActive => VRManager.Instance != null && VRManager.Instance.Active;

        public static void Apply()
        {
            var harmony = new Harmony("berryvr.patches");
            harmony.PatchAll(typeof(VRPatches).Assembly);
            BerryVRPlugin.Log.LogInfo("[VR] Gameplay patches applied.");
        }

        // ----- held object follows the tracked right hand ------------------

        [HarmonyPatch(typeof(PlayerGrabber), "HandleHeldObjectPlacement")]
        private static class PatchHeldPlacement
        {
            private static bool Prefix(PlayerGrabber __instance)
            {
                if (!VRActive) return true;
                if (HeldRb == null || HoldSpot == null || HeldPick == null) return true;

                var rb = HeldRb.GetValue(__instance) as Rigidbody;
                if (rb == null) return true;
                var spot = HoldSpot.GetValue(__instance) as MonoBehaviour;
                var pick = HeldPick.GetValue(__instance) as PickUppable;
                if (spot == null || pick == null) return true;

                float force = HoldForce != null ? (float)HoldForce.GetValue(__instance) : 40f;
                float threshold = TorqueThreshold != null ? (float)TorqueThreshold.GetValue(__instance) : 0.99f;
                float torque = TorqueFacing != null ? (float)TorqueFacing.GetValue(__instance) : 20f;

                Vector3 delta = spot.transform.position - rb.transform.position;
                rb.AddForce(delta * force, ForceMode.Acceleration);

                Vector3 forward = rb.transform.forward;
                Vector3 wanted = VRManager.Instance.RightGrip.rotation *
                                 Quaternion.Euler(pick.held_RotationOffset) * Vector3.forward;
                float dot = Vector3.Dot(forward, wanted);
                if (dot >= threshold)
                {
                    rb.angularVelocity = Vector3.zero;
                    return false;
                }

                Vector3 cross = Vector3.Cross(forward, wanted);
                rb.AddTorque(cross * torque * (1f - dot), ForceMode.Acceleration);
                return false;
            }
        }

        // ----- natural throw: add hand velocity and spin --------------------

        [HarmonyPatch(typeof(PlayerGrabber), "DropHeldItem")]
        private static class PatchDrop
        {
            private static void Prefix(PlayerGrabber __instance, out Rigidbody __state)
            {
                __state = HeldRb != null ? HeldRb.GetValue(__instance) as Rigidbody : null;
            }

            private static void Postfix(Rigidbody __state, bool _Throw)
            {
                if (!VRActive || __state == null) return;
                var vm = VRManager.Instance;

                if (_Throw)
                {
                    Vector3 v = vm.RightThrowVelocity;
                    if (v.magnitude > 0.35f)
                    {
                        v = Vector3.ClampMagnitude(v, 10f) * 0.70f;
                        __state.linearVelocity += v;
                    }
                    __state.angularVelocity += Vector3.ClampMagnitude(vm.RightGripAngularVelocity, 20f) * 0.12f;
                    vm.Input.PulseRight(1500);
                }
                else
                {
                    vm.Input.PulseRight(500);
                }
            }
        }

        // ----- air blast comes from the left hand ---------------------------

        [HarmonyPatch(typeof(PlayerGrabber), "KickInFrontOfUs")]
        private static class PatchKick
        {
            private static void Prefix(PlayerGrabber __instance, out GameObject __state)
            {
                __state = GrabberCam != null ? GrabberCam.GetValue(__instance) as GameObject : null;
                if (!VRActive || __state == null) return;
                if (VRManager.Instance.LeftAim != null)
                    GrabberCam.SetValue(__instance, VRManager.Instance.LeftAim.gameObject);
            }

            private static void Postfix(PlayerGrabber __instance, GameObject __state)
            {
                if (__state != null && GrabberCam != null)
                    GrabberCam.SetValue(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(PlayerGrabber), "AttemptPickup")]
        private static class PatchPickup
        {
            private static void Postfix(PlayerGrabber __instance)
            {
                if (!VRActive) return;
                if (HeldRb != null && HeldRb.GetValue(__instance) != null)
                    VRManager.Instance.Input.PulseRight(1300);
            }
        }

        // ----- the tracked aim indicator replaces the flat crosshairs -------

        [HarmonyPatch(typeof(HudManager), "SetCrossHair")]
        private static class PatchCrosshair
        {
            private static bool Prefix(HudManager __instance)
            {
                if (!VRActive) return true;
                HideCrosshairs(__instance);
                return false;
            }
        }

        public static void HideCrosshairs()
        {
            HideCrosshairs(HudManager.Singleton);
        }

        private static void HideCrosshairs(HudManager hud)
        {
            if (hud == null) return;
            SetInactive(CrosshairBasic != null ? CrosshairBasic.GetValue(hud) as GameObject : null);
            SetInactive(CrosshairUsable != null ? CrosshairUsable.GetValue(hud) as GameObject : null);
            SetInactive(CrosshairMouse != null ? CrosshairMouse.GetValue(hud) as GameObject : null);
        }

        private static void SetInactive(GameObject go)
        {
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        // ----- the compositor owns frame pacing in VR ----------------------

        [HarmonyPatch(typeof(OptionsManager), "ApplySavedSettings")]
        private static class PatchOptionsPerf
        {
            private static void Postfix()
            {
                if (!VRActive) return;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
            }
        }
    }
}
