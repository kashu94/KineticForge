#if KF_META_XR_CORE
using System.Collections.Generic;
#endif
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>Hand joints that Kinetic Forge reads, independent of the skeleton format.</summary>
    public enum HandJoint : byte
    {
        /// <summary>Wrist root.</summary>
        Wrist = 0,

        /// <summary>Thumb tip.</summary>
        ThumbTip = 1,

        /// <summary>Index knuckle (metacarpophalangeal joint).</summary>
        IndexKnuckle = 2,

        /// <summary>Index middle joint (proximal interphalangeal joint).</summary>
        IndexMiddle = 3,

        /// <summary>Index end joint (distal interphalangeal joint).</summary>
        IndexEnd = 4,

        /// <summary>Index tip.</summary>
        IndexTip = 5,

        /// <summary>Middle finger knuckle.</summary>
        MiddleKnuckle = 6,

        /// <summary>Middle finger tip.</summary>
        MiddleTip = 7,

        /// <summary>Ring finger tip.</summary>
        RingTip = 8,
    }

    /// <summary>
    /// Allocation-free reader for one tracked hand: caches the bone transforms of a Meta <c>OVRSkeleton</c> hand and
    /// returns world-space joint positions by <see cref="HandJoint"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Meta ships two hand skeleton formats whose bone ids overlap numerically: the legacy OVR hand (HandLeft, HandRight)
    /// and the OpenXR hand (XRHandLeft, XRHandRight). The id set is picked by skeleton type, and the cache is rebuilt when
    /// the skeleton changes.
    /// </para>
    /// <para>Without the Meta XR Core SDK (KF_META_XR_CORE) the reader never reports a tracked hand.</para>
    /// </remarks>
    public sealed class HandSkeletonReader
    {
        const int JointCount = 9;

        readonly Transform[] _joints = new Transform[JointCount];

#if KF_META_XR_CORE
        OVRSkeleton _skeleton;
        OVRHand _hand;
        bool _cached;
        int _cachedChangedCount;
#endif

        /// <summary>Hand side this reader is bound to.</summary>
        public ForgeHand Hand { get; private set; }

        /// <summary>True when a skeleton is bound.</summary>
        public bool IsBound
        {
            get
            {
#if KF_META_XR_CORE
                return _skeleton != null;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// True while the bound hand has valid, high-confidence tracking data this frame and every joint is cached.
        /// </summary>
        public bool IsTracked
        {
            get
            {
#if KF_META_XR_CORE
                OVRSkeleton skeleton = _skeleton;
                if (skeleton == null || !skeleton.isActiveAndEnabled || !skeleton.IsInitialized || !skeleton.IsDataValid ||
                    !skeleton.IsDataHighConfidence)
                {
                    return false;
                }

                if (_hand != null && !_hand.IsTracked)
                {
                    return false;
                }

                return EnsureCache();
#else
                return false;
#endif
            }
        }

#if KF_META_XR_CORE
        /// <summary>The bound skeleton, or null.</summary>
        public OVRSkeleton Skeleton => _skeleton;

        /// <summary>The OVRHand next to the bound skeleton, or null. Provides pinch strength and microgestures.</summary>
        public OVRHand OvrHand => _hand;

        /// <summary>Binds a hand skeleton. Pass null to unbind.</summary>
        /// <param name="skeleton">A hand <c>OVRSkeleton</c>, or null.</param>
        /// <param name="hand">Hand side of the skeleton.</param>
        public void Bind(OVRSkeleton skeleton, ForgeHand hand)
        {
            _skeleton = skeleton;
            _hand = skeleton != null ? skeleton.GetComponent<OVRHand>() : null;
            Hand = hand;
            _cached = false;
        }

        /// <summary>
        /// Finds the hand skeleton for <paramref name="hand"/> in the loaded scenes. Allocates once; call at startup or
        /// from a slow retry, never per frame.
        /// </summary>
        /// <param name="hand">Left or Right.</param>
        /// <returns>The skeleton, or null.</returns>
        public static OVRSkeleton FindHandSkeleton(ForgeHand hand)
        {
            OVRSkeleton[] skeletons = Object.FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < skeletons.Length; i++)
            {
                if (HandOf(skeletons[i].GetSkeletonType()) == hand)
                {
                    return skeletons[i];
                }
            }

            return null;
        }

        /// <summary>Maps a skeleton type to its hand side, or <see cref="ForgeHand.None"/> for non-hand skeletons.</summary>
        /// <param name="type">Skeleton type.</param>
        /// <returns>The hand side.</returns>
        public static ForgeHand HandOf(OVRSkeleton.SkeletonType type)
        {
            switch (type)
            {
                case OVRSkeleton.SkeletonType.HandLeft:
                case OVRSkeleton.SkeletonType.XRHandLeft:
                    return ForgeHand.Left;
                case OVRSkeleton.SkeletonType.HandRight:
                case OVRSkeleton.SkeletonType.XRHandRight:
                    return ForgeHand.Right;
                default:
                    return ForgeHand.None;
            }
        }
#endif

        /// <summary>Returns the world position of a joint of the tracked hand.</summary>
        /// <param name="joint">Joint to read.</param>
        /// <param name="position">World-space position.</param>
        /// <returns>True when the hand is tracked and the joint is available and finite.</returns>
        public bool TryGetJoint(HandJoint joint, out Vector3 position)
        {
            Transform bone = _joints[(int)joint];
            if (bone == null)
            {
                position = default;
                return false;
            }

            position = bone.position;
            return ForgeMath.IsFinite(position);
        }

#if KF_META_XR_CORE
        bool EnsureCache()
        {
            if (_cached && _cachedChangedCount == _skeleton.SkeletonChangedCount)
            {
                return true;
            }

            System.Array.Clear(_joints, 0, JointCount);
            IList<OVRBone> bones = _skeleton.Bones;
            if (bones == null || bones.Count == 0)
            {
                _cached = false;
                return false;
            }

            OVRSkeleton.SkeletonType type = _skeleton.GetSkeletonType();
            bool openXr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            for (int i = 0; i < bones.Count; i++)
            {
                OVRBone bone = bones[i];
                if (bone == null || bone.Transform == null)
                {
                    continue;
                }

                int slot = openXr ? OpenXrSlot(bone.Id) : OvrSlot(bone.Id);
                if (slot >= 0)
                {
                    _joints[slot] = bone.Transform;
                }
            }

            for (int i = 0; i < JointCount; i++)
            {
                if (_joints[i] == null)
                {
                    _cached = false;
                    return false;
                }
            }

            _cachedChangedCount = _skeleton.SkeletonChangedCount;
            _cached = true;
            return true;
        }

        static int OvrSlot(OVRSkeleton.BoneId id)
        {
            switch (id)
            {
                case OVRSkeleton.BoneId.Hand_WristRoot: return (int)HandJoint.Wrist;
                case OVRSkeleton.BoneId.Hand_ThumbTip: return (int)HandJoint.ThumbTip;
                case OVRSkeleton.BoneId.Hand_Index1: return (int)HandJoint.IndexKnuckle;
                case OVRSkeleton.BoneId.Hand_Index2: return (int)HandJoint.IndexMiddle;
                case OVRSkeleton.BoneId.Hand_Index3: return (int)HandJoint.IndexEnd;
                case OVRSkeleton.BoneId.Hand_IndexTip: return (int)HandJoint.IndexTip;
                case OVRSkeleton.BoneId.Hand_Middle1: return (int)HandJoint.MiddleKnuckle;
                case OVRSkeleton.BoneId.Hand_MiddleTip: return (int)HandJoint.MiddleTip;
                case OVRSkeleton.BoneId.Hand_RingTip: return (int)HandJoint.RingTip;
                default: return -1;
            }
        }

        static int OpenXrSlot(OVRSkeleton.BoneId id)
        {
            // OpenXR ids share numeric values with the OVR hand set, so they are compared in their own switch.
            switch (id)
            {
                case OVRSkeleton.BoneId.XRHand_Wrist: return (int)HandJoint.Wrist;
                case OVRSkeleton.BoneId.XRHand_ThumbTip: return (int)HandJoint.ThumbTip;
                case OVRSkeleton.BoneId.XRHand_IndexProximal: return (int)HandJoint.IndexKnuckle;
                case OVRSkeleton.BoneId.XRHand_IndexIntermediate: return (int)HandJoint.IndexMiddle;
                case OVRSkeleton.BoneId.XRHand_IndexDistal: return (int)HandJoint.IndexEnd;
                case OVRSkeleton.BoneId.XRHand_IndexTip: return (int)HandJoint.IndexTip;
                case OVRSkeleton.BoneId.XRHand_MiddleProximal: return (int)HandJoint.MiddleKnuckle;
                case OVRSkeleton.BoneId.XRHand_MiddleTip: return (int)HandJoint.MiddleTip;
                case OVRSkeleton.BoneId.XRHand_RingTip: return (int)HandJoint.RingTip;
                default: return -1;
            }
        }
#endif
    }
}
