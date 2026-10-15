using System.Collections.Generic;
using UnityEngine;

namespace KinoVR
{
    /// <summary>Follows the SDK index-tip bone without moving or resizing the ball catcher.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class KinoIndexFingerTouch : MonoBehaviour
    {
        HandCatcher catcher;
        OVRHand hand;
        OVRSkeleton skeleton;
        Transform indexTip;
        IList<OVRBone> boundBones;
        OVRSkeleton.SkeletonType boundType;
        SphereCollider contact;
        Rigidbody body;

        void Awake()
        {
            catcher = GetComponent<HandCatcher>();
            hand = GetComponentInParent<OVRHand>(true);
            if (!hand) return;
            skeleton = hand.GetComponent<OVRSkeleton>();
            if (!skeleton) return;
            var target = new GameObject("Index fingertip UI touch");
            // A sibling of HandCatcher: its trigger must never invoke ball catches.
            target.transform.SetParent(hand.transform, false);
            contact = target.AddComponent<SphereCollider>();
            target.AddComponent<KinoFingerTip>();
            contact.enabled = false;
            body = target.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        void LateUpdate() => RefreshTracking();
        void FixedUpdate() => RefreshTracking();

        void RefreshTracking()
        {
            if (!contact) return;
            bool tracked = catcher && catcher.isActiveAndEnabled && hand && hand.isActiveAndEnabled &&
                hand.IsTracked && hand.IsDataValid && hand.IsDataHighConfidence && !hand.IsSystemGestureInProgress &&
                skeleton && skeleton.isActiveAndEnabled && skeleton.IsInitialized && skeleton.IsDataValid;
            if (!tracked) { contact.enabled = false; return; }
            if (!indexTip || !ReferenceEquals(boundBones, skeleton.Bones) || boundType != skeleton.GetSkeletonType())
            {
                boundBones = skeleton.Bones;
                boundType = skeleton.GetSkeletonType();
                indexTip = FindIndexTip(boundType, boundBones);
            }
            if (!indexTip) { contact.enabled = false; return; }
            // Copy the actual tracked bone position, including hand scale and rig pose.
            // The collider keeps a physical 8 mm radius regardless of hand/rig scale.
            var parentScale = contact.transform.parent.lossyScale;
            contact.transform.localScale = new Vector3(1 / Mathf.Max(.00001f, Mathf.Abs(parentScale.x)),
                1 / Mathf.Max(.00001f, Mathf.Abs(parentScale.y)), 1 / Mathf.Max(.00001f, Mathf.Abs(parentScale.z)));
            body.position = indexTip.position;
            body.rotation = Quaternion.identity;
            contact.enabled = true;
        }

        internal static Transform FindIndexTip(OVRSkeleton.SkeletonType type, IList<OVRBone> bones)
        {
            var id = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight
                ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            if (type != OVRSkeleton.SkeletonType.HandLeft && type != OVRSkeleton.SkeletonType.HandRight &&
                type != OVRSkeleton.SkeletonType.XRHandLeft && type != OVRSkeleton.SkeletonType.XRHandRight) return null;
            if (bones != null)
                foreach (var bone in bones)
                    if (bone != null && bone.Id == id) return bone.Transform;
            return null;
        }

        void OnDisable() { if (contact) contact.enabled = false; }
        void OnDestroy()
        {
            if (!contact) return;
            if (Application.isPlaying) Destroy(contact.gameObject); else DestroyImmediate(contact.gameObject);
        }
    }
}
