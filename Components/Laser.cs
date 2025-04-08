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

#nullable disable
        Hand hand;
        Material laserMaterial;
        Transform LaserBeam;
        Interactable lastInteract;
        Transform indexTip;
        int sign;
        Transform hitPoint;
        bool justEntered = false;
        public static Vector3 LastHit;
#nullable restore

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

            //todo get a cool laser material from zigga?
            //renderer.material = laserMaterial;

            GameObject.DestroyImmediate(laserBeamGO.GetComponent<CapsuleCollider>());
            GameObject.DestroyImmediate(hitPoint.GetComponent<SphereCollider>());

            laserBeamGO.SetActive(false);
            hitPoint.gameObject.SetActive(false);
            LaserBeam = laserBeamGO.transform;
        }

        protected void Update()
        {
            if (Physics.Raycast(indexTip.position, sign * indexTip.right, out var hit, 3f, LayerMask.GetMask("UI", "Character", "Ragdolls", "InteractiveItems")))
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
                if(hand.otherHand.hoveringInteractable == null)
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
