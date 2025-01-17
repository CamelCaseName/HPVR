using Il2CppInterop.Runtime.Injection;
using MelonLoader;
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
        Transform hitPoint;

        protected void Awake()
        {
            hand = GetComponent<Hand>();
            sign = hand.handType == SteamVR_Input_Sources.LeftHand ? -1 : 1;

            var laserBeamGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            DontDestroyOnLoad(laserBeamGO);
            indexTip = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.indexTip);
            laserBeamGO.transform.parent = indexTip;
            laserBeamGO.transform.localScale = new(0.005f, 2, 0.005f);
            laserBeamGO.transform.localPosition = new(sign * 2, 0, 0);
            laserBeamGO.transform.localEulerAngles = new(0, 0, 90);
            laserBeamGO.name = name + "LaserPointer";

            var hitGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DontDestroyOnLoad(hitGO);
            hitPoint = hitGO.transform;
            hitPoint.localScale = new(0.01f, 0.01f, 0.01f);

            var renderer = laserBeamGO.GetComponent<MeshRenderer>();
            renderer.material.shader = Shader.Find("HDRP/Lit");
            renderer.material.color = Color.white;

            var hitRenderer = hitPoint.GetComponent<MeshRenderer>();
            hitRenderer.material.shader = Shader.Find("HDRP/Lit");
            hitRenderer.material.color = Color.white;

            //renderer.material = laserMaterial;

            GameObject.DestroyImmediate(laserBeamGO.GetComponent<CapsuleCollider>());
            GameObject.DestroyImmediate(hitPoint.GetComponent<SphereCollider>());

            laserBeamGO.SetActive(false);
            hitPoint.gameObject.SetActive(false);
            LaserBeam = laserBeamGO.transform;
        }

        protected void Update()
        {
            if (Physics.Raycast(indexTip.position, sign * indexTip.right, out var hit, 3f, LayerMask.GetMask("UI", "Character", "Ragdolls")))
            {
                var interact = hit.transform.gameObject.GetComponent<Interactable>();
                interact ??= hit.transform.gameObject.GetComponentInParent<Interactable>();
                interact ??= hit.transform.gameObject.GetComponentInChildren<Interactable>();
                if (interact is null)
                {
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);
                    return;
                }

                if (hand.hoveringInteractable == lastInteract && lastInteract != interact && lastInteract is not null)
                {
                    hand.HoverUnlock(lastInteract);
                }
                if (hand.hoveringInteractable != interact)
                {
                    hand.HoverLock(interact);
                }
                lastInteract = interact;

                hitPoint.position = hit.point;
                LaserBeam.localScale = new(0.005f, hit.distance / 2, 0.005f);
                LaserBeam.localPosition = new(sign * (hit.distance / 2), 0, 0);
                LaserBeam.gameObject.SetActive(true);
                hitPoint.gameObject.SetActive(true);
            }
            else
            {
                if (hand.hoveringInteractable == lastInteract && lastInteract is not null)
                {
                    hand.HoverUnlock(lastInteract);
                    lastInteract = null!;
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);
                }
            }

        }
    }
}
