using Il2CppEekCharacterEngine;
using Il2CppEekUI;
using Il2CppInterop.Runtime.Injection;
using Il2CppRootMotion.Dynamics;
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
        public Interactable lastInteract;
        int sign;
        Transform hitPoint;
        Transform LaserRoot;
        bool justEntered = false;
        public static RaycastHit LastHit;
        public int LaserMask = LayerMask.GetMask("Default", "UI", "InteractiveItems", "InteractiveItemsHighlighted", "Ragdolls", "Ground", "Walls");
        public static readonly int DefaultLaserMask = LayerMask.GetMask("Default", "UI", "Ragdolls", "InteractiveItems", "InteractiveItemsHighlighted");
#nullable restore
        internal Laser? otherLaser;

        protected void Awake()
        {
            hand = GetComponent<Hand>();
            sign = hand.handType == SteamVR_Input_Sources.LeftHand ? -1 : 1;

            //rootgo is attached to hand root, rootgo.forward is forward out of the fingers. more or less
            var laserRootGO = new GameObject("LaserRoot");
            laserRootGO.transform.parent = hand.skeleton.GetBone((int)SteamVR_Skeleton_JointIndexEnum.root).parent;
            laserRootGO.transform.localPosition = new Vector3(sign * 0.04f, -0.043f, 0);
            laserRootGO.transform.localEulerAngles = new Vector3(30, sign * -5, 0);
            LaserRoot = laserRootGO.transform;

            var laserBeamGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            DontDestroyOnLoad(laserBeamGO);
            laserBeamGO.transform.parent = laserRootGO.transform;
            laserBeamGO.transform.localScale = new(0.005f, 2, 0.005f);
            laserBeamGO.transform.localPosition = new(0, 0, 2);
            laserBeamGO.transform.localEulerAngles = new(90, 0, 0);
            laserBeamGO.name = name + " LaserPointer";

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
            if (LaserRoot is null)
            {
                return;
            }
            if (otherLaser is null)
            {
                return;
            }
            if (LaserBeam is null)
            {
                return;
            }

            if (Physics.Raycast(LaserRoot.position, LaserRoot.forward, out var hit, 3f, LaserMask))
            {
                var interact = hit.transform.GetComponent<Interactable>();
                interact ??= hit.transform.GetComponentInParent<Interactable>();
                interact ??= hit.transform.GetComponentInChildren<Interactable>();

                //MelonLogger.Msg("hit " + hit.transform.name);

                //may be a character
                if (interact is null && hit.transform.GetComponentInParent<PuppetMaster>() is not null)
                {
                    var trans = hit.transform;
                    int i = 0;
                    while (trans.parent != null && i++ < 10)
                    {
                        trans = trans.parent;
                    }
                    interact = trans.GetComponentInChildren<Interactable>();
                }

                if (interact is null)
                {
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);

                    if (HPVR.Instance?.inGameMain ?? false)
                    {
                        //disable radial if clicked
                        if (hand.uiInteractAction != null && hand.uiInteractAction.stateUp && !hand.otherHand.hoverLocked && RadialMenu.Singleton.IsShowing)
                        {
                            MelonLogger.Msg("make radial go away");
                            RadialMenu.Singleton.Toggle();
                        }
                    }
                    hand.hoveringInteractable = null;
                    return;
                }

                //we do hit the buttons at this point...

                if (hand.hoveringInteractable == lastInteract && lastInteract != interact && lastInteract is not null)
                {
                    hand.HoverUnlock(lastInteract);
                }
                if (hand.hoveringInteractable != interact)
                {
                    justEntered = true;
                    if (hand.otherHand.hoveringInteractable is null)
                    {
                        hand.HoverLock(interact);
                    }
                    else
                    {
                        //MelonLogger.Msg($"{hand} got blocked by other hand from hovering {interact.gameObject.name}");
                    }
                }
                lastInteract = interact;

                hitPoint.position = hit.point;
                LastHit = hit;
                if (hand.ObjectIsAttached(interact.gameObject))
                {
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);
                }
                else
                {
                    LaserBeam.localScale = new(0.005f, hit.distance / 2, 0.005f);
                    LaserBeam.localPosition = new(0, 0, (hit.distance / 2));
                    LaserBeam.gameObject.SetActive(true);
                    hitPoint.gameObject.SetActive(true);
                }

                var ui = lastInteract.GetComponent<UIElement>();
                if (ui is not null)
                {
                    if (hand.otherHand.hoveringInteractable is null)
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
            }
            else
            {
                if (hand.otherHand.hoveringInteractable == null)
                {
                    LastHit = new();
                }
                if (lastInteract is not null)
                {
                    hand.HoverUnlock(lastInteract);
                    hand.hoveringInteractable = null;
                    justEntered = false;
                    lastInteract = null!;
                    LaserBeam.gameObject.SetActive(false);
                    hitPoint.gameObject.SetActive(false);
                }
                if (HPVR.Instance?.inGameMain ?? false)
                {
                    //disable radial if clicked
                    if (hand.uiInteractAction != null && hand.uiInteractAction.stateUp && !hand.otherHand.hoverLocked && RadialMenu.Singleton.IsShowing)
                    {
                        MelonLogger.Msg("make radial go away");
                        RadialMenu.Singleton.Toggle();
                    }
                }
            }
        }

        public static Vector3 WorldToUISpace(Canvas parentCanvas, Vector3 worldPos) => parentCanvas?.transform?.InverseTransformPoint(worldPos) ?? Vector3.zero;
    }
}
