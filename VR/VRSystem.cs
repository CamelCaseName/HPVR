using HPVR.Gameplay;
using HPVR.UI;
using HPVR.utils;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using SteamVR_Melon.Standalone;
using SteamXR_Melon;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR.VR
{
    //loads all vr plugins and handles headset and controller movement
    internal static class VRSystem
    {
        static private TrackedDevicePose_t[] poses = Array.Empty<TrackedDevicePose_t>();
        public static bool MovementEnabled = true;
        private static bool SetUpInput = false;
        static private GameObject? leftController = null;
        static private GameObject? rightController = null;
        static private Vector3 lastControllerMove = new();
        static private GameObject vrPlayer = null!;
        static private GameObject SteamVRobject = null!;
        static private readonly bool debug = true;
#nullable disable
        static private Hand leftHand;
        static private Hand rightHand;
#nullable enable
        private static Quaternion vrCamRotation = Quaternion.identity;
        static private AssetBundle? bundle;
        static private string fallback_fist = string.Empty;
        static private string fallback_point = string.Empty;
        static private string fallback_relaxed = string.Empty;
        static private LayerMask defaultHandMask = LayerMask.GetMask("Default", "UI", "Walls", "Ground", "Character", "Ragdolls", "InteractiveItems");
        static private Vector3 hmdAbsoluteLastPosition = new();
        static private Vector3 hmdRotationPositionOffset = new();
        static private Vector3 vrCamPosition = new(0, 1.75f, 0);

        public static float Deadzone = 0.0f;
        public static float speed = 0.5f;
        private static bool rotated;

        public static bool Initialized { get; private set; }

        public static bool HandInputActive => leftHand.grabGripAction.stateDown
            || leftHand.grabPinchAction.stateDown
            || leftHand.uiInteractAction.stateDown
            || rightHand.grabGripAction.stateDown
            || rightHand.grabPinchAction.stateDown
            || rightHand.uiInteractAction.stateDown;

        static public void StartVR()
        {
            poses = new TrackedDevicePose_t[4];
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
            SteamVR.enabled = true;
            if (SteamVR.instance is null)
            {
                SteamVR.SafeDispose();
                throw new NotSupportedException("VR Headset was not connected before starting the game");
            }
            //update offset depending on unity version
            PluginImporter.UpdateOffsetForUnityVersion();
            MelonXR.Initialize();
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
                Initialized = true;
            }
            else
            {
                Player.instance.headCollider = SetUpCamera();
            }
        }

        public static void Update()
        {
            if (MovementEnabled)
            {
                HandleControllerMovement();
            }
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
            vrPlayer = new GameObject("VR Player");
            Object.DontDestroyOnLoad(vrPlayer);

            //we need a steamvr player as well for the hands :(
            SteamVRobject = new GameObject("SteamVR");
            SteamVRobject.transform.parent = vrPlayer.transform;
            MelonLogger.Msg("Created SteamVR Gameobject Container");

            var player = vrPlayer.AddComponent<Player>();
            player.trackingOriginTransform = vrPlayer.transform;
            MelonLogger.Msg(Camera.main?.ToString() ?? "camera isnull");
            player.hmdTransforms = new Transform[] { Camera.main.transform };
            player.audioListener = Camera.main.transform;
            player.headCollider = SetUpCamera();
            player.rigSteamVR = SteamVRobject;
            player.headsetOnHead = SteamVR_Actions.default_HeadsetOnHead;
            player.allowToggleTo2D = false;
            MelonLogger.Msg("Created SteamVR Player");

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

        private static SphereCollider SetUpCamera()
        {
            Camera.main.gameObject.AddComponent<SteamVR_Camera>();
            var headCollider = Camera.main.gameObject.AddComponent<SphereCollider>();
            headCollider.radius = 0.05f;
            headCollider.isTrigger = false;
            headCollider.providesContacts = false;
            headCollider.excludeLayers = LayerMask.GetMask("Character", "Ragdolls", "InteractiveItemsHighlighted", "InteractiveItems");
            Camera.main.gameObject.AddComponent<CameraFader>();
            Camera.main.gameObject.AddComponent<SteamVR_Fade>();

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                Object.DestroyImmediate(eekCam);
            }

            Player.instance.hmdTransforms = new Transform[] { Camera.main.transform };
            Player.instance.audioListener = Camera.main.transform;

            return headCollider;
        }

        static private void SetUpControllers()
        {
            if (bundle is null)
            {
                MelonLogger.Warning("Assetbundle is null");
                return;
            }

            leftController = new GameObject("Controller (left)");
            leftController.transform.position = new(0.25f, 1, 0);
            leftController.transform.parent = SteamVRobject.transform;
            leftController.layer = LayerMask.NameToLayer("Penis");
            var leftHoverSphere = new GameObject("HoverPoint");
            var leftObjectAttachement = new GameObject("ObjectAttachement");
            leftHoverSphere.transform.position = new(0.052f, -0.016f, -0.1163f);
            leftHoverSphere.transform.parent = leftController.transform;
            leftObjectAttachement.transform.rotation = Quaternion.Euler(135, -170, -90);
            leftObjectAttachement.transform.position = new(0.052f, -0.0157f, -0.1163f);
            leftObjectAttachement.transform.parent = leftController.transform;

            //create the "prefabs"
            var leftControllerPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/controller.prefab").Cast<GameObject>());
            var leftModel = leftControllerPrefab.AddComponent<SteamVR_RenderModel>();
            leftModel.index = SteamVR_TrackedObject.EIndex.Device1;
            leftModel.modelOverride = string.Empty;
            leftModel.shader = null;
            leftModel.verbose = debug;
            leftModel.createComponents = true;
            leftModel.updateDynamically = true;
            MelonLogger.Warning("built the leftcontrollerprefab");

            var rightControllerPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/controller.prefab").Cast<GameObject>());
            var rightModel = rightControllerPrefab.AddComponent<SteamVR_RenderModel>();
            rightModel.index = SteamVR_TrackedObject.EIndex.Device2;
            rightModel.modelOverride = string.Empty;
            rightModel.shader = null;
            rightModel.verbose = debug;
            rightModel.createComponents = true;
            rightModel.updateDynamically = true;
            MelonLogger.Warning("built the rightcontrollerprefab");

            var vrGloveLeftModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/vr_glove_left_model_slim.prefab").Cast<GameObject>());
            var vrGloveLeftFallback = vrGloveLeftModelSlimPrefab.transform.GetChild(1).gameObject;
            var vrLeftFallback = vrGloveLeftFallback.AddComponent<SteamVR_Skeleton_Poser>();
            Il2CppHelper.MakeBehaviour(fallback_relaxed, out SteamVR_Skeleton_Pose? fallback_relaxed_asset);
            Il2CppHelper.MakeBehaviour(fallback_fist, out SteamVR_Skeleton_Pose? fallback_fist_asset);
            Il2CppHelper.MakeBehaviour(fallback_point, out SteamVR_Skeleton_Pose? fallback_point_asset);
            vrLeftFallback.skeletonMainPose = fallback_relaxed_asset;
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrLeftFallback.Initialize();
            Object.DontDestroyOnLoad(vrGloveLeftModelSlimPrefab);
            MelonLogger.Warning("built the vrleftfallbackposer");

            var skinnedMesh = vrGloveLeftModelSlimPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var meshObj = bundle.LoadAsset("assets/steamvr/models/vr_glove_left_model_slim.fbx", Il2CppType.Of<Mesh>()).Cast<Mesh>();
            skinnedMesh.sharedMesh = meshObj;
            vrGloveLeftModelSlimPrefab.transform.GetChild(0).localScale = Vector3.one;

            var vrLeftGloveSkeleton = vrGloveLeftModelSlimPrefab.AddComponent<SteamVR_Behaviour_Skeleton>();
            vrLeftGloveSkeleton.skeletonAction = SteamVR_Actions.default_SkeletonLeftHand;
            vrLeftGloveSkeleton.inputSource = SteamVR_Input_Sources.LeftHand;
            vrLeftGloveSkeleton.rangeOfMotion = EVRSkeletalMotionRange.WithoutController;
            vrLeftGloveSkeleton.skeletonRoot = vrGloveLeftModelSlimPrefab.transform.GetChild(0).GetChild(0);
            vrLeftGloveSkeleton.origin = null!;
            vrLeftGloveSkeleton.updatePose = true;
            vrLeftGloveSkeleton.onlySetRotations = false;
            vrLeftGloveSkeleton.skeletonBlend = 1;
            vrLeftGloveSkeleton.mirroring = SteamVR_Behaviour_Skeleton.MirrorType.None;
            vrLeftGloveSkeleton.fallbackPoser = vrLeftFallback;
            vrLeftGloveSkeleton.fallbackCurlAction = SteamVR_Actions.default_Squeeze;
            vrLeftGloveSkeleton.Initialize();
            vrLeftGloveSkeleton.FinishInit();
            MelonLogger.Warning("built the vrgloveleftmodelslimprefab");

            var LeftRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/leftrendermodel slim.prefab").Cast<GameObject>());
            var leftRenderModel = LeftRenderModelSlimPrefab.AddComponent<RenderModel>();
            leftRenderModel.controllerPrefab = leftControllerPrefab;
            leftRenderModel.displayControllerByDefault = false;
            leftRenderModel.displayHandByDefault = true;
            leftRenderModel.handPrefab = vrGloveLeftModelSlimPrefab;
            leftRenderModel.Initialize();
            leftRenderModel.InitAction();
            Object.DontDestroyOnLoad(LeftRenderModelSlimPrefab);
            MelonLogger.Warning("built the leftrendermodelslimprefab");

            var handColliderLeftPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderleft.prefab").Cast<GameObject>());
            var handColliderLeft = handColliderLeftPrefab.AddComponent<HandCollider>();
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
            MelonLogger.Warning("built the handcolliderprefab");

            var leftPose = leftController.AddComponent<SteamVR_Behaviour_Pose>();
            leftPose.transform = leftController.transform;
            leftPose.poseAction = SteamVR_Actions.default_Pose;
            leftPose.inputSource = SteamVR_Input_Sources.LeftHand;
            leftPose.broadcastDeviceChanges = true;
            leftPose.Init();
            leftPose.FinishInit();
            MelonLogger.Warning("built the left hand behaviour pose");

            leftHand = leftController.AddComponent<Hand>();
            leftHand.otherHand = null;
            leftHand.handType = SteamVR_Input_Sources.LeftHand;
            leftHand.trackedObject = null;
            leftHand.grabPinchAction = SteamVR_Actions.default_GrabPinch;
            leftHand.grabGripAction = SteamVR_Actions.default_GrabGrip;
            leftHand.hapticAction = SteamVR_Actions.default_Haptic;
            leftHand.uiInteractAction = SteamVR_Actions.default_InteractUI;
            leftHand.useHoverSphere = true;
            leftHand.hoverSphereTransform = leftHoverSphere.transform;
            leftHand.hoverSphereRadius = 0.075f;
            leftHand.hoverLayerMask = defaultHandMask;
            leftHand.hoverUpdateInterval = 0.5f;
            leftHand.useControllerHoverComponent = false;
            leftHand.controllerHoverComponent = "tip";
            leftHand.controllerHoverRadius = 0.15f;
            leftHand.useFingerJointHover = true;
            leftHand.fingerJointHover = SteamVR_Skeleton_JointIndexEnum.indexTip;
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
            MelonLogger.Warning("built the left hand hand");

            var leftPhysics = leftController.AddComponent<HandPhysics>();
            leftPhysics.pose = leftPose;
            leftPhysics.hand = leftHand;
            leftPhysics.Initialize(handColliderLeftPrefab);
            MelonLogger.Warning("built the left hand physics");

            Object.DontDestroyOnLoad(leftController);

            rightController = new GameObject("Controller (right)");
            rightController.transform.position = new(0.25f, 1, 0);
            rightController.transform.parent = SteamVRobject.transform;
            rightController.layer = LayerMask.NameToLayer("Penis");
            var rightHoverSphere = new GameObject("HoverPoint");
            var rightObjectAttachement = new GameObject("ObjectAttachement");
            rightHoverSphere.transform.position = new(0.052f, -0.016f, -0.1163f);
            rightHoverSphere.transform.parent = rightController.transform;
            rightObjectAttachement.transform.rotation = Quaternion.Euler(135, -170, -90);
            rightObjectAttachement.transform.position = new(0.052f, -0.0157f, -0.1163f);
            rightObjectAttachement.transform.parent = rightController.transform;

            var vrGloverightModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/vr_glove_right_model_slim.prefab").Cast<GameObject>());
            var vrGloverightFallback = vrGloverightModelSlimPrefab.transform.GetChild(1).gameObject;
            var vrRightFallback = vrGloverightFallback.AddComponent<SteamVR_Skeleton_Poser>();
            vrRightFallback.skeletonMainPose = fallback_relaxed_asset;
            vrRightFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrRightFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrRightFallback.Initialize();
            Object.DontDestroyOnLoad(vrGloverightModelSlimPrefab);
            MelonLogger.Warning("built the vrrightfallbackposer");

            var vrRightGloveSkeleton = vrGloverightModelSlimPrefab.AddComponent<SteamVR_Behaviour_Skeleton>();
            vrRightGloveSkeleton.skeletonAction = SteamVR_Actions.default_SkeletonRightHand;
            vrRightGloveSkeleton.inputSource = SteamVR_Input_Sources.RightHand;
            vrRightGloveSkeleton.rangeOfMotion = EVRSkeletalMotionRange.WithoutController;
            vrRightGloveSkeleton.skeletonRoot = vrGloverightModelSlimPrefab.transform.GetChild(0).GetChild(0);
            vrRightGloveSkeleton.origin = null!;
            vrRightGloveSkeleton.updatePose = true;
            vrRightGloveSkeleton.onlySetRotations = false;
            vrRightGloveSkeleton.skeletonBlend = 1;
            vrRightGloveSkeleton.mirroring = SteamVR_Behaviour_Skeleton.MirrorType.None;
            vrRightGloveSkeleton.fallbackPoser = vrRightFallback;
            vrRightGloveSkeleton.fallbackCurlAction = SteamVR_Actions.default_Squeeze;
            vrRightGloveSkeleton.Initialize();
            vrRightGloveSkeleton.FinishInit();
            MelonLogger.Warning("built the vrgloverightmodelslimprefab");

            var rightRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/rightrendermodel slim.prefab").Cast<GameObject>());
            var rightRenderModel = rightRenderModelSlimPrefab.AddComponent<RenderModel>();
            rightRenderModel.controllerPrefab = rightControllerPrefab;
            rightRenderModel.displayControllerByDefault = false;
            rightRenderModel.displayHandByDefault = true;
            rightRenderModel.handPrefab = vrGloverightModelSlimPrefab;
            rightRenderModel.Initialize();
            rightRenderModel.InitAction();
            Object.DontDestroyOnLoad(rightRenderModelSlimPrefab);
            MelonLogger.Warning("built the rightrendermodelslimprefab");

            var handColliderrightPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderright.prefab").Cast<GameObject>());
            var handColliderRight = handColliderrightPrefab.AddComponent<HandCollider>();
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
            MelonLogger.Warning("built the handcolliderprefab");

            var rightPose = rightController.AddComponent<SteamVR_Behaviour_Pose>();
            rightPose.transform = rightController.transform;
            rightPose.poseAction = SteamVR_Actions.default_Pose;
            rightPose.inputSource = SteamVR_Input_Sources.RightHand;
            rightPose.broadcastDeviceChanges = true;
            rightPose.Init();
            rightPose.FinishInit();
            MelonLogger.Warning("built the right hand behaviour pose");

            rightHand = rightController.AddComponent<Hand>();
            rightHand.handType = SteamVR_Input_Sources.RightHand;
            rightHand.trackedObject = null;
            rightHand.grabPinchAction = SteamVR_Actions.default_GrabPinch;
            rightHand.grabGripAction = SteamVR_Actions.default_GrabGrip;
            rightHand.hapticAction = SteamVR_Actions.default_Haptic;
            rightHand.uiInteractAction = SteamVR_Actions.default_InteractUI;
            rightHand.useHoverSphere = true;
            rightHand.hoverSphereTransform = rightHoverSphere.transform;
            rightHand.hoverSphereRadius = 0.075f;
            rightHand.hoverLayerMask = defaultHandMask;
            rightHand.hoverUpdateInterval = 0.5f;
            rightHand.useControllerHoverComponent = false;
            rightHand.controllerHoverComponent = "tip";
            rightHand.controllerHoverRadius = 0.15f;
            rightHand.useFingerJointHover = true;
            rightHand.fingerJointHover = SteamVR_Skeleton_JointIndexEnum.indexTip;
            rightHand.fingerJointHoverRadius = 0.05f;
            rightHand.objectAttachmentPoint = rightObjectAttachement.transform;
            rightHand.noSteamVRFallbackCamera = null;
            rightHand.noSteamVRFallbackMaxDistanceNoItem = 10;
            rightHand.noSteamVRFallbackMaxDistanceWithItem = 0.5f;
            rightHand.renderModelPrefab = rightRenderModelSlimPrefab;
            rightHand.spewDebugText = debug;
            rightHand.trackedObject = rightPose;
            rightHand.OnHandInitialized += (int i) => { rightHand.gameObject.AddComponent<Laser>(); };
            rightHand.Initialize();
            rightHand.FinishInit();
            MelonCoroutines.Start(rightHand.Start());
            MelonLogger.Warning("built the right hand hand");

            rightHand.otherHand = leftHand;
            leftHand.otherHand = rightHand;

            var rightPhysics = rightController.AddComponent<HandPhysics>();
            rightPhysics.pose = rightPose;
            rightPhysics.hand = rightHand;
            rightPhysics.Initialize(handColliderrightPrefab);
            MelonLogger.Warning("built the right hand physics");

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

        private static void HandleControllerMovement()
        {
            vrPlayer.transform.position += new Vector3(lastControllerMove.x, 0, lastControllerMove.z);

            lastControllerMove = Vector3.zero;
        }

        private static void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow();
            poses = new TrackedDevicePose_t[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            //velocity is always 0 :(
            //MelonLogger.Msg($"headset velocity: {seconds} {velocity.x}|{velocity.y}|{velocity.z}");

            if (rotated)
            {
                hmdRotationPositionOffset = hmdAbsoluteLastPosition;
                //keep height, but move "center" to new spot under the headset, so we can offset the real world space offset the player had from there and then apply the virtual rotation onyl to the difference we have
                vrPlayer.transform.position = new(vrCamPosition.x, vrPlayer.transform.position.y, vrCamPosition.z);
                rotated = false;
            }
            hmdAbsoluteLastPosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            //todo how about we use the velocities here [m/s]? this would eliminate the weird offsets by just getting the changes and diffs, but decoupled hopefully in their axis
            vrCamRotation = vrPlayer.transform.rotation * poses[0].mDeviceToAbsoluteTracking.GetRotation();
            //when using only velocities we have to add the height manually
            Vector3 locationDifference = (hmdAbsoluteLastPosition - hmdRotationPositionOffset);
            locationDifference.y = 0;
            vrCamPosition = vrPlayer.transform.position + (vrPlayer.transform.rotation * locationDifference) + new Vector3(0, hmdAbsoluteLastPosition.y, 0);

            SteamVR_Camera.instance.transform.rotation = vrCamRotation;
            SteamVR_Camera.instance.transform.position = vrCamPosition;

        }

        private static float PredictSecondsFromNow()
        {
            float fSecondsSinceLastVsync = 0.0f;
            ulong zero = 0;
            ETrackedPropertyError error = 0;
            OpenVR.System.GetTimeSinceLastVsync(ref fSecondsSinceLastVsync, ref zero);
            float fDisplayFrequency = OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref error);
            float fFrameDuration = 1.0f / fDisplayFrequency;
            float fVsyncToPhotons = OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float, ref error);
            return fFrameDuration - fSecondsSinceLastVsync + fVsyncToPhotons;
        }

        private static void SetUpSteamActionsIfNeeded()
        {
            SteamVR_Actions._default.Activate();
            SteamVR_Actions.platformer_Move.actionSet.Activate(priority: 1);

            if (!SteamVR_Actions._default.IsActive() || !SteamVR_Actions.platformer_Move.actionSet.IsActive() || SetUpInput)
            {
                if (!SetUpInput)
                {
                    MelonLogger.Warning("Could not activate Steam Input, maybe controllers are not turned on?");
                }
                return;
            }

            SteamVR_Actions.platformer_Move.onAxis += (SteamVR_Action_Vector2 fromAction, SteamVR_Input_Sources fromSource, Vector2 axis, Vector2 delta) =>
            {

                if (axis.magnitude > Deadzone)
                {
                    var yRotation = Quaternion.Euler(0, vrCamRotation.eulerAngles.y, 0);
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
                MelonLogger.Msg($"{lastControllerMove.x}{lastControllerMove.z}");
            };

            SteamVR_Actions.default_SnapTurnLeft.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {
                //if (inGameMain && playerChar is not null)
                //{
                //    vrPlayer.transform.position = new(vrCamPosition.x, playerChar.position.y, vrCamPosition.z);
                //}
                //else
                //{
                //    vrPlayer.transform.position = new(vrCamPosition.x, 0, vrCamPosition.z);
                //}
                vrPlayer.transform.rotation *= Quaternion.AngleAxis(-45, Vector3.up);
                rotated = true;
                //UpdateHMDPositions();
            };

            SteamVR_Actions.default_SnapTurnRight.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {

                //if (inGameMain && playerChar is not null)
                //{
                //    vrPlayer.transform.position = new(vrCamPosition.x, playerChar.position.y, vrCamPosition.z);
                //}
                //else
                //{
                //    vrPlayer.transform.position = new(vrCamPosition.x, 0, vrCamPosition.z);
                //}
                vrPlayer.transform.rotation *= Quaternion.AngleAxis(45, Vector3.up);
                rotated = true;
                //UpdateHMDPositions();
            };

            SetUpInput = true;
            MelonLogger.Msg("Activated SteamVR actions");
        }

    }
}
