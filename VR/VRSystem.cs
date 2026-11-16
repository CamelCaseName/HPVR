//#define OASIS_MODE

using HPVR.Gameplay.Behaviours;
using HPVR.utils;
using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekUI;
using Il2CppInterop.Runtime;
using MelonLoader;
using SteamVR_Melon.Standalone;
using SteamXR_Melon;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR.VR
{
    //loads all vr plugins and handles headset and controller movement
    internal static class VRSystem
    {
        public const float gravity = -2f;
        private const float Stepheight = 0.3f;
        public static bool Gravity = true;
        private static TrackedDevicePoseT[] poses = Array.Empty<TrackedDevicePoseT>();
        public static bool MovementEnabled = true;
        internal static bool SetUpInput = false;
        private static GameObject? leftController = null;
        private static GameObject? rightController = null;
        public static Vector3 lastControllerMove = new();
        private static GameObject vrPlayer = null!;
        private static CapsuleCollider playerBody = null!;
        private static GameObject SteamVRobject = null!;
        private static readonly bool debug = true;
#nullable disable
        private static Hand leftHand;
        private static Hand rightHand;
#nullable enable
        private static Quaternion vrCamRotation = Quaternion.identity;
        private static AssetBundle? bundle;
        private static string fallback_fist = string.Empty;
        private static string fallback_point = string.Empty;
        private static string fallback_relaxed = string.Empty;
        private static LayerMask defaultHandMask = LayerMask.GetMask("Default", "UI", "InteractiveItems", "InteractiveItemsHighlighted", "Ragdolls", "Ground", "Walls");
        private static LayerMask defaultHoverMask = LayerMask.GetMask("Default", "InteractiveItems", "InteractiveItemsHighlighted");
        private static Vector3 hmdAbsoluteLastPosition = new();
        private static Vector3 hmdRotationPositionOffset = new();
        private static Vector3 vrCamPosition = new(0, 1.75f, 0);

        public static float Deadzone = 0.0f;
        public static float speed = 0.5f;
        private static float FallTime = 0.01f;
        private static bool Grounded = false;
        private static bool rotated = true;

        private static float angle = 0;
        private static HandCollider? handColliderLeft;
        private static HandCollider? handColliderRight;

        public static bool Initialized { get; private set; }

        public static bool HandInputActive => leftHand.grabGripAction.stateDown
            || leftHand.uiInteractAction.stateDown
            || rightHand.grabGripAction.stateDown
            || rightHand.uiInteractAction.stateDown;

        public static Vector3 HmdAbsoluteLastPosition { get => hmdAbsoluteLastPosition; private set => hmdAbsoluteLastPosition = value; }
        public static Vector3 HmdRotationPositionOffset { get => hmdRotationPositionOffset; private set => hmdRotationPositionOffset = value; }
        public static Vector3 VrCamPosition { get => vrCamPosition; private set => vrCamPosition = value; }
        public static Quaternion VrCamRotation { get => vrCamRotation; private set => vrCamRotation = value; }

        public static void StartVR()
        {
            poses = new TrackedDevicePoseT[4];
            Il2CppHelper.CreateAndSavePlugin("openvr_api");
            Il2CppHelper.CreateAndSavePlugin("XRSDKOpenVR");
            Il2CppHelper.CreateAndSavePlugin("ucrtbased");

            string HousePartyMainLocation = Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName;

            string folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "SteamVR_Melon");
            if (!File.Exists(Path.Combine(folderPath, "actions.json")))
            {
                Directory.CreateDirectory(folderPath);
                foreach (var fullNames in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    var name = fullNames.Split('.')[^2];
                    if (name.StartsWith("bindings_") || name.StartsWith("binding_") || name == "actions")
                    {
                        Il2CppHelper.CreateAndSaveToPath(folderPath, "actions." + name, ".json", name);
                    }
                }
            }

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "UnitySubsystems", "XRSDKOpenVR");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "UnitySubsystemsManifest", ".json");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "SteamVR");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "OpenVRSettings", ".asset");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "HPVR");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "assets.hpvr_assets", ".manifest", "hpvr_assets");
            fallback_fist = Il2CppHelper.CreateAndSaveToPath(folderPath, "assets.fallback_fist", ".asset", "fallback_fist");
            fallback_point = Il2CppHelper.CreateAndSaveToPath(folderPath, "assets.fallback_point", ".asset", "fallback_point");
            fallback_relaxed = Il2CppHelper.CreateAndSaveToPath(folderPath, "assets.fallback_relaxed", ".asset", "fallback_relaxed");
            var assets = Il2CppHelper.CreateAndSaveToPath(folderPath, "assets.hpvr_assets", "", "hpvr_assets");
            bundle = AssetBundle.LoadFromFile(assets);

            RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(SteamVR)));
            RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(MelonXR)));
            //UnityEngine.Rendering.TextureXR.maxViews = 2;
            //do steamvr before melonxr
            SteamVR.Enabled = true;
            if (SteamVR.Instance is null)
            {
                SteamVR.SafeDispose();
                throw new NotSupportedException("VR Headset was not connected before starting the game");
            }
            //update offset depending on unity version
            PluginImporter.UpdateOffsetForUnityVersion();
            MelonXR.Initialize();
        }

        public static void SyncPlayerAndHMD()
        {
            rotated = true;
        }

        public static void SetUpSteamVRUnity()
        {
            if (!Initialized)
            {
                SetUpSteamActionsIfNeeded();
                //set up steamvr objects, camera and stuff
                SetUpSteamVR();
                //set up controller objects
                SetUpControllers();
                //add the hands to the player and start it
                FinalizeSteamVRSetup();

                //UnityEngine.Rendering.XRGraphics.stereoRenderingMode = UnityEngine.Rendering.XRGraphics.StereoRenderingMode.SinglePassInstanced;
                UnityEngine.Rendering.TextureXR.maxViews = 2;

                Initialized = true;
            }
            else
            {
                Player.instance.headCollider = SetUpCamera();
            }
        }

        public static void Update()
        {
            HandleControllerMovement();
            UpdateHMDPositions();
        }

        private static void FinalizeSteamVRSetup()
        {
            var player = vrPlayer.GetComponent<Player>();
            player.hands = new Hand[] { leftHand, rightHand };
            player.Init();
            MelonCoroutines.Start(player.Start());
        }

        private static void SetUpSteamVR()
        {
            vrPlayer = new GameObject("VR Player")
            {
                layer = LayerMask.NameToLayer("Character")
            };
            Object.DontDestroyOnLoad(vrPlayer);

            //we need a steamvr player as well for the hands :(
            SteamVRobject = new GameObject("SteamVR");
            SteamVRobject.transform.parent = vrPlayer.transform;
            SteamVRobject.transform.localPosition = new(0.08f, 0, 0);
            MelonLogger.Msg("Created SteamVR Gameobject Container");

            var player = vrPlayer.AddComponent<Player>();
            var TrackingOrigin = new GameObject("trackingOrigin");
            TrackingOrigin.transform.parent = vrPlayer.transform;
            player.trackingOriginTransform = TrackingOrigin.transform;
            MelonLogger.Msg(Camera.main?.ToString() ?? "camera isnull");
            player.hmdTransforms = new Transform[] { Camera.main!.transform };
            player.audioListener = Camera.main.transform;
            player.headCollider = SetUpCamera();
            player.rigSteamVR = SteamVRobject;
            player.headsetOnHead = SteamVRActions.hpvr_HeadsetOnHead;
            player.allowToggleTo2D = false;
            MelonLogger.Msg("Created SteamVR Player");

            playerBody = vrPlayer.AddComponent<CapsuleCollider>();
            playerBody.radius = 0.25f;
            playerBody.isTrigger = false;
            playerBody.direction = 1;
            playerBody.providesContacts = false;
            playerBody.includeLayers = LayerMask.GetMask("Walls", "Ground", "Ragdolls", "InteractiveItems");
            SetPlayerColliderHeight(1.8f);

            Object.DontDestroyOnLoad(SteamVRobject);

            var input = new GameObject("VRInputModule");
            input.transform.parent = SteamVRobject.transform;
            var eventSystem = input.AddComponent<EventSystem>();
            eventSystem.m_FirstSelected = null;
            eventSystem.sendNavigationEvents = false;
            eventSystem.m_DragThreshold = 0;
            var inputComponent = input.AddComponent<InputModule>();
            inputComponent.sendPointerHoverToParent = true;
            MelonLogger.Msg("Created SteamVR Inputmodule");

            //seems this one is too old?
            //replaced standaloneinputmodule with inputsystemuiinputmodule
            var standalone = input.AddComponent<InputSystemUIInputModule>();
            //read only :(
            //standalone.sendPointerHoverToParent = true;
            standalone.repeatDelay = 0.5f;
            MelonLogger.Msg("Created SteamVR Standalone Container");
        }

        private static void SetPlayerColliderHeight(float height)
        {
            //todo scale such that the real life player is scaled to the in game player height
            height -= 0.3f;
            playerBody.height = height;
            playerBody.center = vrPlayer.transform.position + new Vector3(0, height / 2, 0);
        }

        private static SphereCollider SetUpCamera()
        {
            Camera.main.gameObject.AddComponent<SteamVRCamera>();
            var headCollider = Camera.main.gameObject.AddComponent<SphereCollider>();
            headCollider.radius = 0.05f;
            headCollider.isTrigger = false;
            headCollider.providesContacts = false;
            headCollider.excludeLayers = LayerMask.GetMask("Character", "Ragdolls", "InteractiveItemsHighlighted", "InteractiveItems");
            Camera.main.gameObject.AddComponent<CameraFader>();
            Camera.main.gameObject.AddComponent<SteamVRFade>();

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                Object.DestroyImmediate(eekCam);
            }

            Player.instance.hmdTransforms = new Transform[] { Camera.main.transform };
            Player.instance.audioListener = Camera.main.transform;

            return headCollider;
        }

        private static void SetUpControllers()
        {
            if (bundle is null)
            {
                MelonLogger.Warning("Assetbundle is null");
                return;
            }

            leftController = new GameObject("Controller (left)");
            leftController.transform.parent = SteamVRobject.transform;
            leftController.transform.localPosition = new(0.25f, 1, 0);
            leftController.layer = LayerMask.NameToLayer("Penis");
            var leftHoverSphere = new GameObject("HoverPoint");
            var leftObjectAttachement = new GameObject("ObjectAttachement");
            leftHoverSphere.transform.parent = leftController.transform;
            leftHoverSphere.transform.localPosition = new(0.052f, -0.016f, -0.1163f);
            leftObjectAttachement.transform.parent = leftController.transform;
            leftObjectAttachement.transform.localRotation = Quaternion.Euler(135, -170, -90);
            leftObjectAttachement.transform.localPosition = new(0.052f, -0.0157f, -0.1163f);

            //create the "prefabs"
            var leftControllerPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/controller.prefab").Cast<GameObject>());
            var leftModel = leftControllerPrefab.AddComponent<SteamVRRenderModel>();
            leftModel.index = SteamVRTrackedObject.EIndex.Device1;
            leftModel.modelOverride = string.Empty;
            leftModel.shader = null;
            leftModel.verbose = debug;
            leftModel.createComponents = true;
            leftModel.updateDynamically = true;
            MelonLogger.Msg("built the leftcontrollerprefab");

            var rightControllerPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/controller.prefab").Cast<GameObject>());
            var rightModel = rightControllerPrefab.AddComponent<SteamVRRenderModel>();
            rightModel.index = SteamVRTrackedObject.EIndex.Device2;
            rightModel.modelOverride = string.Empty;
            rightModel.shader = null;
            rightModel.verbose = debug;
            rightModel.createComponents = true;
            rightModel.updateDynamically = true;
            MelonLogger.Msg("built the rightcontrollerprefab");

            var vrGloveLeftModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/vr_glove_left_model_slim.prefab").Cast<GameObject>());
            var vrGloveLeftFallback = vrGloveLeftModelSlimPrefab.transform.GetChild(1).gameObject;
            var vrLeftFallback = vrGloveLeftFallback.AddComponent<SteamVRSkeletonPoser>();
            Il2CppHelper.MakeBehaviour(fallback_relaxed, out SteamVRSkeletonPose? fallback_relaxed_asset);
            Il2CppHelper.MakeBehaviour(fallback_fist, out SteamVRSkeletonPose? fallback_fist_asset);
            Il2CppHelper.MakeBehaviour(fallback_point, out SteamVRSkeletonPose? fallback_point_asset);
            vrLeftFallback.skeletonMainPose = fallback_relaxed_asset;
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrLeftFallback.Initialize();
            Object.DontDestroyOnLoad(vrGloveLeftModelSlimPrefab);
            MelonLogger.Msg("built the vrleftfallbackposer");

            //seems this mesh is correct for correctly supported headsets, oasis needs the older one??
#if !OASIS_MODE
            var skinnedMesh = vrGloveLeftModelSlimPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var meshObj = bundle.LoadAsset("assets/steamvr/models/vr_glove_left_model_slim.fbx", Il2CppType.Of<Mesh>()).Cast<Mesh>();
            skinnedMesh.sharedMesh = meshObj;
            vrGloveLeftModelSlimPrefab.transform.GetChild(0).localScale = Vector3.one;
#else
            var skinnedMesh = vrGloveLeftModelSlimPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var meshObj = bundle.LoadAsset("assets/steamvr/models/vr_glove_right_model_slim.fbx", Il2CppType.Of<Mesh>()).Cast<Mesh>();
            skinnedMesh.sharedMesh = meshObj;
            vrGloveLeftModelSlimPrefab.transform.GetChild(0).localScale = Vector3.one;
#endif

            var vrLeftGloveSkeleton = vrGloveLeftModelSlimPrefab.AddComponent<SteamVRBehaviourSkeleton>();
            vrLeftGloveSkeleton.skeletonAction = SteamVRActions.hpvr_SkeletonLeftHand;
            vrLeftGloveSkeleton.inputSource = SteamVRInputSources.LeftHand;
            vrLeftGloveSkeleton.rangeOfMotion = EVRSkeletalMotionRange.WithoutController;
            vrLeftGloveSkeleton.skeletonRoot = vrGloveLeftModelSlimPrefab.transform.GetChild(0).GetChild(0);
            vrLeftGloveSkeleton.origin = null!;
            vrLeftGloveSkeleton.updatePose = true;
            vrLeftGloveSkeleton.onlySetRotations = false;
            vrLeftGloveSkeleton.skeletonBlend = 1;
            vrLeftGloveSkeleton.mirroring = SteamVRBehaviourSkeleton.MirrorType.None;
            vrLeftGloveSkeleton.fallbackPoser = vrLeftFallback;
            vrLeftGloveSkeleton.Initialize();
            vrLeftGloveSkeleton.FinishInit();
            MelonLogger.Msg("built the vrgloveleftmodelslimprefab");

            var LeftRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/leftrendermodel slim.prefab").Cast<GameObject>());
            var leftRenderModel = LeftRenderModelSlimPrefab.AddComponent<RenderModel>();
            leftRenderModel.controllerPrefab = leftControllerPrefab;
            leftRenderModel.displayControllerByDefault = false;
            leftRenderModel.displayHandByDefault = true;
            leftRenderModel.handPrefab = vrGloveLeftModelSlimPrefab;
            leftRenderModel.Initialize();
            leftRenderModel.InitAction();
#if OASIS_MODE
            LeftRenderModelSlimPrefab.transform.localScale = new(-1, 1, 1);
#endif
            Object.DontDestroyOnLoad(LeftRenderModelSlimPrefab);
            MelonLogger.Msg("built the leftrendermodelslimprefab");

            var handColliderLeftPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderleft.prefab").Cast<GameObject>());
            handColliderLeft = handColliderLeftPrefab.AddComponent<HandCollider>();
            handColliderLeft.collisionMask = defaultHandMask;
            handColliderLeft.fingerColliders = new();
            handColliderLeft.fingerColliders.thumbColliders[0] = handColliderLeftPrefab.transform.GetChild(1).GetChild(0);
            handColliderLeft.fingerColliders.indexColliders[0] = handColliderLeftPrefab.transform.GetChild(1).GetChild(1);
            handColliderLeft.fingerColliders.indexColliders[1] = handColliderLeftPrefab.transform.GetChild(1).GetChild(2);
            handColliderLeft.fingerColliders.indexColliders[2] = handColliderLeftPrefab.transform.GetChild(1).GetChild(3);
            handColliderLeft.fingerColliders.middleColliders[0] = handColliderLeftPrefab.transform.GetChild(1).GetChild(4);
            handColliderLeft.fingerColliders.middleColliders[1] = handColliderLeftPrefab.transform.GetChild(1).GetChild(5);
            handColliderLeft.fingerColliders.middleColliders[2] = handColliderLeftPrefab.transform.GetChild(1).GetChild(6);
            handColliderLeft.fingerColliders.ringColliders[0] = handColliderLeftPrefab.transform.GetChild(1).GetChild(7);
            handColliderLeft.fingerColliders.ringColliders[1] = handColliderLeftPrefab.transform.GetChild(1).GetChild(8);
            handColliderLeft.fingerColliders.pinkyColliders[0] = handColliderLeftPrefab.transform.GetChild(1).GetChild(9);
            handColliderLeft.fingerColliders.pinkyColliders[1] = handColliderLeftPrefab.transform.GetChild(1).GetChild(10);
            handColliderLeft.collidersInRadius = false;
            Object.DontDestroyOnLoad(handColliderLeft);
            MelonLogger.Msg("built the handcolliderprefab");

            var leftPose = leftController.AddComponent<SteamVRBehaviourPose>();
            leftPose.transform = leftController.transform;
            leftPose.poseAction = SteamVRActions.hpvr_Pose;
            leftPose.inputSource = SteamVRInputSources.LeftHand;
            leftPose.broadcastDeviceChanges = true;
            leftPose.Init();
            leftPose.FinishInit();
            MelonLogger.Msg("built the left hand behaviour pose");

            leftHand = leftController.AddComponent<Hand>();
            leftHand.otherHand = null;
            leftHand.handType = SteamVRInputSources.LeftHand;
            leftHand.trackedObject = null;
            leftHand.grabGripAction = SteamVRActions.hpvr_Grab;
            leftHand.hapticAction = SteamVRActions.hpvr_LeftHaptic;
            leftHand.uiInteractAction = SteamVRActions.hpvr_InteractUI;
            leftHand.useHoverSphere = true;
            leftHand.hoverSphereTransform = leftHoverSphere.transform;
            leftHand.hoverSphereRadius = 0.15f;
            leftHand.hoverLayerMask = defaultHoverMask;
            leftHand.hoverUpdateInterval = 0.5f;
            leftHand.useControllerHoverComponent = false;
            leftHand.controllerHoverComponent = "tip";
            leftHand.controllerHoverRadius = 0.15f;
            leftHand.useFingerJointHover = true;
            leftHand.fingerJointHover = SteamVRSkeletonJointIndexEnum.indexTip;
            leftHand.fingerJointHoverRadius = 0.05f;
            leftHand.objectAttachmentPoint = leftObjectAttachement.transform;
            leftHand.noSteamVRFallbackCamera = null;
            leftHand.noSteamVRFallbackMaxDistanceNoItem = 10;
            leftHand.noSteamVRFallbackMaxDistanceWithItem = 0.5f;
            leftHand.renderModelPrefab = LeftRenderModelSlimPrefab;
            leftHand.spewDebugText = debug;
            leftHand.trackedObject = leftPose;
            leftHand.Initialize();
            leftHand.FinishInit();
            MelonCoroutines.Start(leftHand.Start());
            MelonLogger.Msg("built the left hand hand");

            var leftPhysics = leftController.AddComponent<HandPhysics>();
            leftPhysics.pose = leftPose;
            leftPhysics.hand = leftHand;
            leftPhysics.Initialize(handColliderLeftPrefab);
            MelonLogger.Msg("built the left hand physics");

            Object.DontDestroyOnLoad(leftController);

            rightController = new GameObject("Controller (right)");
            rightController.transform.parent = SteamVRobject.transform;
            rightController.transform.localPosition = new(0.25f, 1, 0);
            rightController.layer = LayerMask.NameToLayer("Penis");
            var rightHoverSphere = new GameObject("HoverPoint");
            var rightObjectAttachement = new GameObject("ObjectAttachement");
            rightHoverSphere.transform.parent = rightController.transform;
            rightHoverSphere.transform.localPosition = new(0.052f, -0.016f, -0.1163f);
            rightObjectAttachement.transform.parent = rightController.transform;
            rightObjectAttachement.transform.localRotation = Quaternion.Euler(135, -170, -90);
            rightObjectAttachement.transform.localPosition = new(0.052f, -0.0157f, -0.1163f);

            var vrGloverightModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/vr_glove_right_model_slim.prefab").Cast<GameObject>());
            var vrGloverightFallback = vrGloverightModelSlimPrefab.transform.GetChild(1).gameObject;
            var vrRightFallback = vrGloverightFallback.AddComponent<SteamVRSkeletonPoser>();
            vrRightFallback.skeletonMainPose = fallback_relaxed_asset;
            vrRightFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrRightFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrRightFallback.Initialize();
            Object.DontDestroyOnLoad(vrGloverightModelSlimPrefab);
            MelonLogger.Msg("built the vrrightfallbackposer");

            var vrRightGloveSkeleton = vrGloverightModelSlimPrefab.AddComponent<SteamVRBehaviourSkeleton>();
            vrRightGloveSkeleton.skeletonAction = SteamVRActions.hpvr_SkeletonRightHand;
            vrRightGloveSkeleton.inputSource = SteamVRInputSources.RightHand;
            vrRightGloveSkeleton.rangeOfMotion = EVRSkeletalMotionRange.WithoutController;
            vrRightGloveSkeleton.skeletonRoot = vrGloverightModelSlimPrefab.transform.GetChild(0).GetChild(0);
            vrRightGloveSkeleton.origin = null!;
            vrRightGloveSkeleton.updatePose = true;
            vrRightGloveSkeleton.onlySetRotations = false;
            vrRightGloveSkeleton.skeletonBlend = 1;
            vrRightGloveSkeleton.mirroring = SteamVRBehaviourSkeleton.MirrorType.None;
            vrRightGloveSkeleton.fallbackPoser = vrRightFallback;
            vrRightGloveSkeleton.Initialize();
            vrRightGloveSkeleton.FinishInit();
            MelonLogger.Msg("built the vrgloverightmodelslimprefab");

            var rightRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/rightrendermodel slim.prefab").Cast<GameObject>());
            var rightRenderModel = rightRenderModelSlimPrefab.AddComponent<RenderModel>();
            rightRenderModel.controllerPrefab = rightControllerPrefab;
            rightRenderModel.displayControllerByDefault = false;
            rightRenderModel.displayHandByDefault = true;
            rightRenderModel.handPrefab = vrGloverightModelSlimPrefab;
            rightRenderModel.Initialize();
            rightRenderModel.InitAction();
            Object.DontDestroyOnLoad(rightRenderModelSlimPrefab);
            MelonLogger.Msg("built the rightrendermodelslimprefab");

            var handColliderrightPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderright.prefab").Cast<GameObject>());
            handColliderRight = handColliderrightPrefab.AddComponent<HandCollider>();
            handColliderRight.collisionMask = defaultHandMask;
            handColliderRight.fingerColliders = new();
            handColliderRight.fingerColliders.thumbColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(0);
            handColliderRight.fingerColliders.indexColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(1);
            handColliderRight.fingerColliders.indexColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(2);
            handColliderRight.fingerColliders.indexColliders[2] = handColliderrightPrefab.transform.GetChild(1).GetChild(3);
            handColliderRight.fingerColliders.middleColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(4);
            handColliderRight.fingerColliders.middleColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(5);
            handColliderRight.fingerColliders.middleColliders[2] = handColliderrightPrefab.transform.GetChild(1).GetChild(6);
            handColliderRight.fingerColliders.ringColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(7);
            handColliderRight.fingerColliders.ringColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(8);
            handColliderRight.fingerColliders.pinkyColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(9);
            handColliderRight.fingerColliders.pinkyColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(10);
            handColliderRight.collidersInRadius = false;
            Object.DontDestroyOnLoad(handColliderRight);
            MelonLogger.Msg("built the handcolliderprefab");

            var rightPose = rightController.AddComponent<SteamVRBehaviourPose>();
            rightPose.transform = rightController.transform;
            rightPose.poseAction = SteamVRActions.hpvr_Pose;
            rightPose.inputSource = SteamVRInputSources.RightHand;
            rightPose.broadcastDeviceChanges = true;
            rightPose.Init();
            rightPose.FinishInit();
            MelonLogger.Msg("built the right hand behaviour pose");

            rightHand = rightController.AddComponent<Hand>();
            rightHand.handType = SteamVRInputSources.RightHand;
            rightHand.trackedObject = null;
            rightHand.grabGripAction = SteamVRActions.hpvr_Grab;
            rightHand.hapticAction = SteamVRActions.hpvr_RightHaptic;
            rightHand.uiInteractAction = SteamVRActions.hpvr_InteractUI;
            rightHand.useHoverSphere = true;
            rightHand.hoverSphereTransform = rightHoverSphere.transform;
            rightHand.hoverSphereRadius = 0.15f;
            rightHand.hoverLayerMask = defaultHoverMask;
            rightHand.hoverUpdateInterval = 0.5f;
            rightHand.useControllerHoverComponent = false;
            rightHand.controllerHoverComponent = "tip";
            rightHand.controllerHoverRadius = 0.15f;
            rightHand.useFingerJointHover = true;
            rightHand.fingerJointHover = SteamVRSkeletonJointIndexEnum.indexTip;
            rightHand.fingerJointHoverRadius = 0.05f;
            rightHand.objectAttachmentPoint = rightObjectAttachement.transform;
            rightHand.noSteamVRFallbackCamera = null;
            rightHand.noSteamVRFallbackMaxDistanceNoItem = 10;
            rightHand.noSteamVRFallbackMaxDistanceWithItem = 0.5f;
            rightHand.renderModelPrefab = rightRenderModelSlimPrefab;
            rightHand.spewDebugText = debug;
            rightHand.trackedObject = rightPose;
            rightHand.Initialize();
            rightHand.FinishInit();
            MelonCoroutines.Start(rightHand.Start());
            MelonLogger.Msg("built the right hand hand");

            rightHand.otherHand = leftHand;
            leftHand.otherHand = rightHand;

            var rightPhysics = rightController.AddComponent<HandPhysics>();
            rightPhysics.pose = rightPose;
            rightPhysics.hand = rightHand;
            rightPhysics.Initialize(handColliderrightPrefab);
            MelonLogger.Msg("built the right hand physics");

            Object.DontDestroyOnLoad(rightController);
        }

        /*
         assets/steamvr/interactionsystem/core/prefabs/handcolliderleft.prefab
         assets/steamvr/interactionsystem/core/prefabs/handcolliderright.prefab
         assets/steamvr/interactionsystem/core/prefabs/leftrendermodel alien.prefab
         assets/steamvr/interactionsystem/core/prefabs/leftrendermodel slim.prefab
         assets/steamvr/interactionsystem/core/prefabs/leftrendermodel.prefab
         assets/steamvr/interactionsystem/core/prefabs/leftrendermodelfloppy.prefab
         assets/steamvr/interactionsystem/core/prefabs/player.prefab
         assets/steamvr/interactionsystem/core/prefabs/rightrendermodel alien.prefab
         assets/steamvr/interactionsystem/core/prefabs/rightrendermodel slim.prefab
         assets/steamvr/interactionsystem/core/prefabs/rightrendermodel.prefab
         assets/steamvr/interactionsystem/core/prefabs/rightrendermodelfloppy.prefab
         assets/steamvr/interactionsystem/samples/prefabs/throwableball.prefab
         assets/steamvr/interactionsystem/samples/prefabs/throwablecube.prefab
         assets/steamvr/interactionsystem/samples/squishy/squishy.prefab
         assets/steamvr/models/handfingers.mask
         assets/steamvr/models/materials/vr_glove_color.jpg
         assets/steamvr/models/materials/vr_glove_color.mat
         assets/steamvr/models/materials/vr_glove_color_red.jpg
         assets/steamvr/models/materials/vr_glove_color_red.mat
         assets/steamvr/models/materials/vr_glove_normal.png
         assets/steamvr/models/vr_glove_graspposes.controller
         assets/steamvr/models/vr_glove_left_model_slim.fbx
         assets/steamvr/models/vr_glove_model.fbx
         assets/steamvr/models/vr_glove_right_model_slim.fbx
         assets/steamvr/models/vr_hand_grabposes.fbx
         assets/steamvr/prefabs/[camerarig].prefab
         assets/steamvr/prefabs/[steamvr].prefab
         assets/steamvr/prefabs/controller.prefab
         assets/steamvr/prefabs/vr_glove_left.prefab
         assets/steamvr/prefabs/vr_glove_left_model_slim.prefab
         assets/steamvr/prefabs/vr_glove_right.prefab
         assets/steamvr/prefabs/vr_glove_right_model_slim.prefab
         assets/steamvr/resources/steamvr_externalcamera.prefab
         */

        public static void EnableRightCollider()
        {
            handColliderRight?.transform.GetChild(0).gameObject.SetActive(true);
            handColliderRight?.transform.GetChild(1).gameObject.SetActive(true);
        }

        public static void EnableLeftCollider()
        {
            handColliderLeft?.transform.GetChild(0).gameObject.SetActive(true);
            handColliderLeft?.transform.GetChild(1).gameObject.SetActive(true);
        }

        public static void DisableRightCollider()
        {
            handColliderRight?.transform.GetChild(0).gameObject.SetActive(false);
            handColliderRight?.transform.GetChild(1).gameObject.SetActive(false);
        }

        public static void DisableLeftCollider()
        {
            handColliderLeft?.transform.GetChild(0).gameObject.SetActive(false);
            handColliderLeft?.transform.GetChild(1).gameObject.SetActive(false);
        }

        private static void HandleControllerMovement()
        {
            if (!MovementEnabled)
            {
                return;
            }

            var directionXZ = new Vector3(lastControllerMove.x, 0, lastControllerMove.z);
            Vector3 normalizedXZ = directionXZ.normalized;
            var directionY = new Vector3(0, gravity * FallTime, 0);
            Vector3 supposedMove = Vector3.zero;
            bool feetHadHit = false;
            bool headHadHit = false;
            bool stepHadHit = false;

            //start cast outside player hull
            //feet
            if (Physics.Raycast(vrPlayer.transform.position + new Vector3(0, Stepheight, 0), directionXZ, out RaycastHit feetInfo, directionXZ.magnitude + playerBody.radius, playerBody.includeLayers))
            {
                //check if we hit the hands, we can iognore them
                if (feetInfo.transform.root != PlayerCharacter.Player.transform.root && feetInfo.transform.root != vrPlayer)
                {
                    MelonLogger.Msg("hit the " + feetInfo.transform.root.name + " with the feet");
                    feetHadHit = true;
                }
            }

            //head
            if (Physics.Raycast(VrCamPosition, directionXZ, out RaycastHit headInfo, directionXZ.magnitude + playerBody.radius, playerBody.includeLayers))
            {
                if (headInfo.transform.root != PlayerCharacter.Player.transform.root && headInfo.transform.root != vrPlayer)
                {
                    MelonLogger.Msg("hit the " + headInfo.transform.root.name + " with the head");
                    headHadHit = true;
                }
            }

            //higher check
            if (Physics.Raycast(vrPlayer.transform.position + new Vector3(0, 2 * Stepheight, 0), directionXZ, out RaycastHit stepInfo, directionXZ.magnitude + playerBody.radius, playerBody.includeLayers))
            {
                //ignore self collision
                if (stepInfo.transform.root != PlayerCharacter.Player.transform.root && stepInfo.transform.root != vrPlayer)
                {
                    MelonLogger.Msg("hit the " + stepInfo.transform.root.name + " with the step");
                    stepHadHit = true;
                }
            }

            var adjRadius = playerBody.radius + 0.03f;
            //no hits at all, we can just move
            if (!feetHadHit && !headHadHit && !stepHadHit)
            {
                supposedMove += directionXZ;
            }
            else if (feetHadHit && !headHadHit && !stepHadHit)
            {
                supposedMove += normalizedXZ * (feetInfo.distance - adjRadius);
                //MelonLogger.Msg($"2: {feetInfo.distance - adjRadius}");
            }
            else if (!feetHadHit && headHadHit && !stepHadHit)
            {
                supposedMove += normalizedXZ * (headInfo.distance - adjRadius);
                //MelonLogger.Msg($"3: {headInfo.distance - adjRadius}");
            }
            else if (feetHadHit && headHadHit && !stepHadHit)
            {
                //use smaller value so we dont bump into things on overhang or slope
                var dist = Mathf.Min(feetInfo.distance, headInfo.distance);
                supposedMove += normalizedXZ * (dist - adjRadius);
                //MelonLogger.Msg($"4: {dist - adjRadius}");
            }
            else if (!feetHadHit && !headHadHit && stepHadHit)
            {
                supposedMove += normalizedXZ * (stepInfo.distance - adjRadius);
                //MelonLogger.Msg($"5: {stepInfo.distance - adjRadius}");
            }
            else if (feetHadHit && !headHadHit && stepHadHit)
            {
                var dist = Mathf.Min(feetInfo.distance, stepInfo.distance);
                supposedMove += normalizedXZ * (dist - adjRadius);
                //MelonLogger.Msg($"6: {dist - adjRadius}");
            }
            else if (!feetHadHit && headHadHit && stepHadHit)
            {
                var dist = Mathf.Min(headInfo.distance, stepInfo.distance);
                supposedMove += normalizedXZ * (dist - adjRadius);
                //MelonLogger.Msg($"7: {dist - adjRadius}");
            }
            else if (feetHadHit && headHadHit && stepHadHit)
            {
                var dist = Mathf.Min(feetInfo.distance, headInfo.distance, stepInfo.distance);
                supposedMove += normalizedXZ * (dist - adjRadius);
                //MelonLogger.Msg($"8: {dist - adjRadius}");
            }

            //start ground sweep a little inside player, above ground
            if (Gravity)
            {
                var lift = new Vector3(0, 0.5f, 0);
                if (Physics.Raycast(vrPlayer.transform.position + lift, directionY, out RaycastHit groundInfo, (directionY.magnitude * 2) + lift.y, playerBody.includeLayers))
                {
                    //we just got on the ground or are below push up, 
                    if (!Grounded || (vrPlayer.transform.position.y - groundInfo.point.y) < 0)
                    {
                        supposedMove += directionY.normalized * (groundInfo.distance - lift.y + 0.01f);
                        //MelonLogger.Msg($"9: {groundInfo.distance}");
                    }
                    Grounded = true;
                }
                else
                {
                    Grounded = false;
                    supposedMove += directionY;
                }
            }

            //MelonLogger.Msg($" controller {lastControllerMove.x}:{lastControllerMove.z} | supposed {supposedMove.x}:{supposedMove.y}:{supposedMove.z}");

            //finally execute all movement
            vrPlayer.transform.position += supposedMove;
            //MelonLogger.Msg($" player feet pos {vrPlayer.transform.position.x}:{vrPlayer.transform.position.y}:{vrPlayer.transform.position.z}");

            //Only accelerate downwards if we are falling
            if (!Grounded)
            {
                FallTime += Time.deltaTime;
            }
            else
            {
                FallTime = 0.01f;
            }

            lastControllerMove = Vector3.zero;
        }

        private static void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow() * 2;
            //MelonLogger.Msg("predictd seconds " + seconds);
            poses = new TrackedDevicePoseT[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            //velocity is always 0 :(

            if (rotated)
            {
                //HmdRotationPositionOffset = HmdAbsoluteLastPosition;
                //Vector3 playerFeetOffset = Player.instance.trackingOriginTransform.position - Player.instance.feetPositionGuess;
                //Player.instance.trackingOriginTransform.position -= playerFeetOffset;
                //Player.instance.transform.Rotate(Vector3.up, angle);
                //playerFeetOffset = Quaternion.Euler(0.0f, angle, 0.0f) * playerFeetOffset;
                //Player.instance.trackingOriginTransform.position += playerFeetOffset;
                //rotated = false;
                vrPlayer.transform.rotation *= Quaternion.AngleAxis(angle, Vector3.up);
                HmdRotationPositionOffset = HmdAbsoluteLastPosition;
                //keep height, but move "center" to new spot under the headset, so we can offset the real world space offset the player had from there and then apply the virtual rotation onyl to the difference we have
                vrPlayer.transform.position = new Vector3(VrCamPosition.x, vrPlayer.transform.position.y, VrCamPosition.z);
                SteamVRobject.transform.localPosition = -new Vector3(HmdRotationPositionOffset.x, 0, HmdRotationPositionOffset.z);
                angle = 0;
                rotated = false;
            }
            HmdAbsoluteLastPosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            VrCamRotation = vrPlayer.transform.rotation * poses[0].mDeviceToAbsoluteTracking.GetRotation();
            Vector3 locationDifference = HmdAbsoluteLastPosition - HmdRotationPositionOffset;
            locationDifference.y = 0;
            //reAdd player height
            VrCamPosition = vrPlayer.transform.position + (vrPlayer.transform.rotation * locationDifference) + new Vector3(0, HmdAbsoluteLastPosition.y, 0);

            Player.instance.hands[0].transform.position += new Vector3(0, 0.025f, 0);
            Player.instance.hands[1].transform.position += new Vector3(0, 0.025f, 0);

            SteamVRCamera.instance.transform.rotation = VrCamRotation;
            SteamVRCamera.instance.transform.position = VrCamPosition;

            SetPlayerColliderHeight(HmdAbsoluteLastPosition.y);
        }

        private static float PredictSecondsFromNow()
        {
            float fSecondsSinceLastVsync = 0.0f;
            ulong zero = 0;
            ETrackedPropertyError error = 0;
            OpenVR.System.GetTimeSinceLastVsync(ref fSecondsSinceLastVsync, ref zero);
            float fDisplayFrequency = OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.kUnTrackedDeviceIndexHmd, ETrackedDeviceProperty.PropDisplayFrequencyFloat, ref error);
            float fFrameDuration = 1.0f / fDisplayFrequency;
            float fVsyncToPhotons = OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.kUnTrackedDeviceIndexHmd, ETrackedDeviceProperty.PropSecondsFromVsyncToPhotonsFloat, ref error);
            return fFrameDuration - fSecondsSinceLastVsync + fVsyncToPhotons;
        }

        private static void SetUpSteamActionsIfNeeded()
        {
            SteamVRActions.hpvr.Activate();

            if (!SteamVRActions.hpvr.IsActive() || SetUpInput)
            {
                if (!SetUpInput)
                {
                    MelonLogger.Warning("Could not activate Steam Input, maybe controllers are not turned on?");
                }
                return;
            }

            SteamVRActions.hpvr_Move.onAxis += (fromAction, fromSource, axis, delta) =>
            {
                if (axis.magnitude > Deadzone)
                {
                    var yRotation = Quaternion.Euler(0, VrCamRotation.eulerAngles.y, 0);
                    var moveDirectionForward = yRotation * Vector3.forward;//get the angle of the touch and correct it for the rotation of the controller
                    var moveDirectionSide = yRotation * Vector3.right;//get the angle of the touch and correct it for the rotation of the controller

                    lastControllerMove = (moveDirectionForward * axis.y * (axis.sqrMagnitude / speed * Time.deltaTime))
                        + (moveDirectionSide * axis.x * (axis.sqrMagnitude / speed * Time.deltaTime));
                    lastControllerMove.y = 0;
                }
                else
                {
                    lastControllerMove = Vector3.zero;
                }
            };

            SteamVRActions.hpvr_SnapTurnLeft.onStateDown += (fromAction, fromSource) =>
            {
                //MelonLogger.Msg("rotate left");
                //vrPlayer.transform.rotation *= Quaternion.AngleAxis(-45, Vector3.up);
                angle = -45;
                rotated = true;
            };

            SteamVRActions.hpvr_SnapTurnRight.onStateDown += (fromAction, fromSource) =>
            {
                //MelonLogger.Msg("rotate right");
                //vrPlayer.transform.rotation *= Quaternion.AngleAxis(45, Vector3.up);
                angle = 45;
                rotated = true;
            };

            SteamVRActions.hpvr_GameMenu.onStateDown += (fromAction, fromSource) =>
            {
                GameMenu.Singleton?.Toggle();
            };

            SteamVRActions.hpvr_Inventory.onStateDown += (fromAction, fromSource) =>
            {
                InventoryUI.Singleton?.Toggle();
            };

            SteamVRActions.hpvr_Memories.onStateDown += (fromAction, fromSource) =>
            {
                OpportunityWindowManager.Singleton?.OnMemories();
                OpportunityWindowManager.Singleton?.Toggle();
            };

            SteamVRActions.hpvr_Opportunities.onStateDown += (fromAction, fromSource) =>
            {
                OpportunityWindowManager.Singleton?.OnOpportunity();
                OpportunityWindowManager.Singleton?.Toggle();
            };

            SteamVRActions.hpvr_Self_Radial.onStateDown += (fromAction, fromSource) =>
            {
                if (RadialMenu.Singleton is null || InteractionManager.Singleton is null || PlayerCharacter.Player is null)
                {
                    return;
                }
                RadialInteractable.ToggleRadial(Vector3.zero, PlayerCharacter.Player.Interaction);
            };

            SteamVRActions.hpvr_UI_Radial.onStateDown += (fromAction, fromSource) =>
            {
                if (OpportunityWindowManager.Singleton?.IsShowing ?? false)
                {
                    OpportunityWindowManager.Singleton?.Toggle();
                }
                if (InventoryUI.Singleton?.IsShowing ?? false)
                {
                    InventoryUI.Singleton?.Toggle();
                }
                UIRadialMenu.Singleton?.Toggle();
            };

            SteamVRActions.hpvr_Combat.onStateDown += (fromAction, fromSource) =>
            {
                PlayerCharacter.Player?.OnCombatMode();
            };

            SteamVRActions.hpvr_Crouch.onStateDown += (fromAction, fromSource) =>
            {
                if (PlayerCharacter.Player is null)
                {
                    return;
                }
                PlayerCharacter.Player.IsCrouching = !PlayerCharacter.Player.IsCrouching;
            };

            SteamVRActions.hpvr_Flash.onStateDown += (fromAction, fromSource) =>
            {
                if (PlayerCharacter.Player is null)
                {
                    return;
                }
                if (PlayerCharacter.Player.Gender == Il2CppEekEvents.Genders.Female)
                {
                    PlayerCharacter.Player.OnFlashBreasts();
                }
                else
                {
                    PlayerCharacter.Player.OnPenis();
                }
            };

            SteamVRActions.hpvr_Masturbate.onStateDown += (fromAction, fromSource) =>
            {
                PlayerCharacter.Player?.OnMasturbate();
            };

            SteamVRActions.hpvr_Pee.onStateDown += (fromAction, fromSource) =>
            {
                PlayerCharacter.Player?.OnPee();
            };

            SetUpInput = true;
            MelonLogger.Msg("Activated SteamVR actions");
        }
    }
}
