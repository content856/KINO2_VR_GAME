using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    /// <summary>Offline regression checks for index-tip mapping and every direct-touch menu target.</summary>
    public static class KinoFingerTouchTests
    {
        const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static GameObject CreateFinger(string name)
        {
            var finger = new GameObject(name);
            var collider = finger.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = KinoFingerTip.Radius;
            finger.AddComponent<KinoFingerTip>();
            var body = finger.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return finger;
        }

        public static void ValidateBatch()
        {
            try { Validate(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        [MenuItem("Tools/KINO VR/Validate index fingertip menu touch")]
        public static void Validate()
        {
            ValidateBoneMapping();
            ValidateContacts();
            const string result = "PASS: OpenXR and legacy index-tip mapping on both hands; no wrist fallback; " +
                "wrist/palm rejected by backend, mode/start and restart; 8 mm index tip accepts its own row, " +
                "rejects neighbouring rows, preserves hidden-row release volume, and ignores disabled/lost contacts; " +
                "every menu requires release after an unarmed touch. Physical play fixtures use index tips; ball-catching fixtures retain HandCatcher.\n";
            Directory.CreateDirectory("Artifacts/KinoFingerTouch");
            File.WriteAllText("Artifacts/KinoFingerTouch/validation.txt", result);
            Debug.Log("KINO_FINGER_TOUCH_PASS: " + result);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void ValidateBoneMapping()
        {
            var root = new GameObject("Synthetic hand bone mapping");
            try
            {
                var wrist = new GameObject("Wrist").transform;
                var tip = new GameObject("Index tip").transform;
                wrist.SetParent(root.transform); tip.SetParent(root.transform);
                wrist.position = Vector3.zero; tip.position = new Vector3(.03f, .02f, .16f);
                var lookup = typeof(KinoIndexFingerTouch).GetMethod("FindIndexTip", HiddenStatic);
                foreach (var type in new[] { OVRSkeleton.SkeletonType.HandLeft, OVRSkeleton.SkeletonType.HandRight,
                    OVRSkeleton.SkeletonType.XRHandLeft, OVRSkeleton.SkeletonType.XRHandRight })
                {
                    bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
                    var bones = new List<OVRBone>
                    {
                        new OVRBone(xr ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot, -1, wrist),
                        new OVRBone(xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip, 0, tip)
                    };
                    Check((Transform)lookup.Invoke(null, new object[] { type, bones }) == tip, type + " did not resolve the index tip.");
                    bones.RemoveAt(1);
                    Check(!(Transform)lookup.Invoke(null, new object[] { type, bones }), type + " fell back to a wrist when the tip was absent.");
                }
                Check(!(Transform)lookup.Invoke(null, new object[] { OVRSkeleton.SkeletonType.Body,
                    new List<OVRBone> { new OVRBone(OVRSkeleton.BoneId.Hand_IndexTip, 0, tip) } }), "A body skeleton was accepted as a hand.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateContacts()
        {
            var root = new GameObject("Synthetic menu contact tests");
            try
            {
                // Use millimetre UI coordinates, a rotated panel and an offset collider centre.
                var row = new GameObject("Row 1").AddComponent<BoxCollider>();
                row.transform.SetParent(root.transform);
                row.transform.localScale = Vector3.one * .001f;
                row.transform.SetPositionAndRotation(new Vector3(.5f, 1.2f, 2), Quaternion.Euler(9, 24, 0));
                row.size = new Vector3(554, 49, 32); row.center = new Vector3(4, -3, 0);
                var next = new GameObject("Row 2").AddComponent<BoxCollider>();
                next.transform.SetParent(row.transform, false);
                next.transform.localPosition = new Vector3(0, -58, 0); next.size = row.size; next.center = row.center;
                var finger = CreateFinger("Synthetic index fingertip");
                finger.transform.SetParent(root.transform);
                var contact = finger.GetComponent<SphereCollider>();
                finger.transform.position = row.transform.TransformPoint(row.center);
                var palm = new GameObject("Unchanged ball catcher");
                palm.transform.SetParent(root.transform); palm.transform.position = finger.transform.position;
                palm.AddComponent<HandCatcher>();
                var catchSphere = palm.AddComponent<SphereCollider>(); catchSphere.radius = .1f; catchSphere.isTrigger = true;
                var touch = typeof(KinoBackendTouchButton).GetMethod("IsTouching", HiddenStatic);
                bool Touch(BoxCollider area, Collider source, float extra = 0) =>
                    (bool)touch.Invoke(null, new object[] { area, source, extra });

                Check(!Touch(row, catchSphere), "A wrist/palm still selects a menu row.");
                Check(Touch(row, contact), "The tracked index tip did not select its own row.");
                Check(!Touch(next, contact), "The fingertip selected the neighbouring row.");
                row.gameObject.SetActive(false);
                Check(Touch(row, contact, .012f), "A hidden row discarded the panel's held-fingertip latch.");
                row.gameObject.SetActive(true);
                finger.transform.position += row.transform.right * .31f;
                Check(!Touch(row, contact, .012f), "Leaving a row did not release its fingertip latch.");
                finger.transform.position = row.transform.TransformPoint(row.center);
                contact.enabled = false;
                Check(!Touch(row, contact), "Lost tracking left an active fingertip contact.");
                contact.enabled = true;
                finger.SetActive(false);
                Check(!Touch(row, contact), "An inactive hand left an active fingertip contact.");
                finger.SetActive(true);

                // Exercise the actual trigger handlers, including the unarmed-touch latch.
                var target = new GameObject("Button handlers", typeof(RectTransform), typeof(Image), typeof(Button));
                target.SetActive(false); target.transform.SetParent(root.transform);
                var button = target.GetComponent<Button>();
                var mode = target.AddComponent<KinoExperienceModeButton>(); mode.button = button; mode.pressArea = row;
                var restart = target.AddComponent<KinoRestartButton>(); restart.button = button; restart.pressArea = row;
                var backend = target.AddComponent<KinoBackendTouchButton>();
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.transform.SetParent(target.transform, false);
                typeof(KinoBackendTouchButton).GetMethod("Initialize", HiddenInstance).Invoke(backend, new object[] { null, button, label, row });
                target.SetActive(true);
                foreach (var receiver in new MonoBehaviour[] { backend, mode, restart })
                {
                    var enter = receiver.GetType().GetMethod("OnTriggerEnter", HiddenInstance);
                    var stay = receiver.GetType().GetMethod("OnTriggerStay", HiddenInstance);
                    var exit = receiver.GetType().GetMethod("OnTriggerExit", HiddenInstance);
                    var held = (HashSet<Collider>)receiver.GetType().GetField("releaseBeforePress", HiddenInstance).GetValue(receiver);
                    enter.Invoke(receiver, new object[] { catchSphere });
                    Check(held.Count == 0, receiver.GetType().Name + " accepted a wrist collider.");
                    enter.Invoke(receiver, new object[] { contact });
                    stay.Invoke(receiver, new object[] { contact });
                    Check(held.Count == 1 && held.Contains(contact), receiver.GetType().Name + " did not latch an unarmed index touch.");
                    exit.Invoke(receiver, new object[] { contact });
                    Check(held.Count == 0, receiver.GetType().Name + " did not release the index touch.");
                    enter.Invoke(receiver, new object[] { contact });
                    target.SetActive(false);
                    target.SetActive(true);
                    // These runtime-only behaviours do not receive lifecycle callbacks
                    // in this Edit Mode fixture, just as triggers above are invoked manually.
                    if (!Application.isPlaying)
                        receiver.GetType().GetMethod("OnEnable", HiddenInstance).Invoke(receiver, null);
                    Check(held.Count == 0, receiver.GetType().Name + " retained a stale contact after hiding/reopening.");
                    enter.Invoke(receiver, new object[] { contact });
                    Check(held.Contains(contact), receiver.GetType().Name + " ignored the next session's first touch.");
                    exit.Invoke(receiver, new object[] { contact });
                }
                Check(catchSphere.radius == .1f && palm.GetComponent<HandCatcher>(), "Menu touch changed the ball-catching collider.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
