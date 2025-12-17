using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace HPVR.UI
{
    [RegisterTypeInIl2Cpp]
    internal class Laser : MonoBehaviour
    {
        public Laser(IntPtr value) : base(value) { }

        public Laser() : base(ClassInjector.DerivedConstructorPointer<Laser>()) => ClassInjector.DerivedConstructorBody(this);

#nullable disable
        Hand hand;
#pragma warning disable IDE0051, IDE0044, CS0169 // we'll get a cool material from zigga :D
        Material laserMaterial;
#pragma warning restore IDE0051, IDE0044, CS0169 // Remove unused private members
        Transform LaserBeam;
        Interactable lastInteract;
        int sign;
        Transform hitPoint;
        Transform LaserRoot;
        bool justEntered = false;
        public static Vector3 LastHit;
#nullable restore

        protected void Awake()
        {
            hand = GetComponent<Hand>();
            sign = hand.handType == SteamVR_Input_Sources.LeftHand ? -1 : 1;

            var laserBeamGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            DontDestroyOnLoad(laserBeamGO);
            laserBeamGO.transform.parent = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.indexTip);
            laserBeamGO.transform.localScale = new(0.005f, 2, 0.005f);
            laserBeamGO.transform.localPosition = new(sign * 2, 0, 0);
            laserBeamGO.transform.localEulerAngles = new(0, 0, 90);
            laserBeamGO.name = name + "LaserPointer";
            
            //todo after were set up with the positions, anchor the laser to the hand instead of the finger so the laser doesnt move with the finger on trigger pull
            var laserRootPos = laserBeamGO.transform.position;
            var laserRootGO = new GameObject("LaserRoot");
            laserRootGO.transform.parent = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.root);
            laserRootGO.transform.position = laserRootPos;
            LaserRoot = laserRootGO.transform;

            laserBeamGO.transform.parent = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.root);
            //laserBeamGO.transform.position = laserRootPos;

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

            //todo get a cool laser material from zigga?
            //renderer.material = laserMaterial;

            DestroyImmediate(laserBeamGO.GetComponent<CapsuleCollider>());
            DestroyImmediate(hitPoint.GetComponent<SphereCollider>());

            laserBeamGO.SetActive(false);
            hitPoint.gameObject.SetActive(false);
            LaserBeam = laserBeamGO.transform;
        }

        protected void Update()
        {
            if(LaserRoot is null)
            {
                return;
            }
            if (Physics.Raycast(LaserRoot.position, sign * LaserRoot.right, out var hit, 3f, LayerMask.GetMask("UI", "Character", "Ragdolls", "InteractiveItems")))
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
                    justEntered = true;
                    hand.HoverLock(interact);
                }
                lastInteract = interact;

                hitPoint.position = hit.point;
                LastHit = hit.point;
                LaserBeam.localScale = new(0.005f, hit.distance / 2, 0.005f);
                LaserBeam.localPosition = new(sign * (hit.distance / 2), 0, 0);
                LaserBeam.gameObject.SetActive(true);
                hitPoint.gameObject.SetActive(true);

                var ui = lastInteract.GetComponent<UIElement>();
                if (ui is not null)
                {
                    var screenHit = WorldToUISpace(ui.canvas, hit.point);
                    //var coll = hit.transform.GetComponent<Collider>();
                    //MelonLogger.Msg(screenHit.ToString() + " " + hit.point.ToString() + " " + coll.bounds.center + " " + coll.bounds.min + " " + coll.bounds.max);
                    if (justEntered)
                    {
                        justEntered = false;
                        hand.hoveringInteractable.OnHandHoverBegin_Internal(hand, screenHit, true);
                    }
                    hand.hoveringInteractable.HandHoverUpdate_Internal(hand, screenHit, true);
                }
            }
            else
            {
                if (hand.otherHand.hoveringInteractable == null)
                {
                    LastHit = Vector3.zero;
                }
                if (hand.hoveringInteractable == lastInteract && lastInteract is not null)
                {
                    hand.HoverUnlock(lastInteract);
                    justEntered = false;
                    lastInteract = null!;
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);
                }
            }
        }

        public static Vector3 WorldToUISpace(Canvas parentCanvas, Vector3 worldPos) => parentCanvas?.transform?.InverseTransformPoint(worldPos) ?? Vector3.zero;
    }
}
