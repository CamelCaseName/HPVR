using Il2CppEekCharacterEngine;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using System.Timers;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace HPVR.Components
{
    [RegisterTypeInIl2Cpp]
    internal class Laser : MonoBehaviour
    {
        public Laser(IntPtr value) : base(value) { }

        public Laser() : base(ClassInjector.DerivedConstructorPointer<Laser>()) => ClassInjector.DerivedConstructorBody(this);

        Hand hand;
        Material laserMaterial;
        Transform LaserBeam;
        Interactable lastInteract;
        Transform indexTip;
        int sign;

        protected void Awake()
        {
            hand = GetComponent<Hand>();
            sign = hand.handType == SteamVR_Input_Sources.LeftHand ? -1 : 1;

            var laserBeamGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            indexTip = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.indexTip);
            laserBeamGO.transform.parent = indexTip;
            laserBeamGO.transform.localScale = new(0.01f, 2, 0.01f);
            laserBeamGO.transform.localPosition = new(sign * 2, 0, 0);
            laserBeamGO.transform.localEulerAngles = new(0, 0, 90);
            laserBeamGO.name = name + "LaserPointer";

            var renderer = laserBeamGO.GetComponent<MeshRenderer>();
            renderer.material.shader = Shader.Find("HDRP/Lit");
            renderer.material.color = Color.white;

            //renderer.material = laserMaterial;

            GameObject.DestroyImmediate(laserBeamGO.GetComponent<CapsuleCollider>());

            //laserBeamGO.SetActive(false);
            LaserBeam = laserBeamGO.transform;

            Task.Run(() => { Thread.Sleep(1500); ResetPosition(); });

            //try
            //{
            //    foreach (var m in Material)
            //    {
            //        MelonLogger.Msg($"{m.PackageName} + {m.PackageTag}");
            //        foreach (var t in m.Materials)
            //        {
            //            MelonLogger.Msg($"    {t}");
            //        }
            //    }
            //}
            //catch { }
        }

        protected void ResetPosition()
        {
            //LaserBeam.localScale = new(0.01f, 2, 0.01f);
            //LaserBeam.localPosition = new(-2, 0, 0);
            //LaserBeam.localRotation = Quaternion.EulerAngles(0, 0, 90);
        }

        protected void Update()
        {
            //todo find correct axis
            if (Physics.Raycast(indexTip.position, sign * indexTip.right, out var hit, 3f, LayerMask.GetMask("UI", "Character", "Ragdolls")))
            {
                MelonLogger.Msg(hit.point.ToString());
                var interact = hit.transform.gameObject.GetComponent<Interactable>();
                if (interact is null)
                {
                    return;
                }

                if (hand.hoveringInteractable == lastInteract && lastInteract != interact)
                {
                    hand.HoverUnlock(lastInteract);
                }
                if (hand.hoveringInteractable != interact)
                {
                    hand.HoverLock(interact);
                }
                lastInteract = interact;

                LaserBeam.localScale = new(0.01f, hit.distance, 0.01f);
                LaserBeam.position = new(sign * hit.distance, 0, 0);
                //LaserBeam.gameObject.SetActive(true);
            }
            if (hand.hoveringInteractable == lastInteract && lastInteract is not null)
            {
                hand.HoverUnlock(lastInteract);
                lastInteract = null!;
            }
            //LaserBeam.gameObject.SetActive(false);
        }
    }
}
