using HPVR.Components;
using HPVR.utils;
using Il2Cpp;
using Il2CppCinemachine;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents;
using Il2CppEekEvents.Helper;
using Il2CppHouseParty;
using Il2CppInterop.Runtime;
using Il2CppSimpleColorPicker.Scripts;
using Il2CppTMPro;
using MelonLoader;
using SteamXR_Melon;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR
{
    public class HPVR : MelonMod
    {
        private bool colliding = false;
        private bool inGameMain = false;
        private bool inMainMenu = false;
        private bool inLoadingScreen;
        private bool inDisclaimer;
        private bool SetUpInput = false;
        private bool removedPlayerHead = false;
        private GameObject? leftController = null;
        private GameObject? rightController = null;
#nullable disable
        private Hand leftHand;
        private Hand rightHand;
#nullable enable
        private Quaternion vrCamRotation = Quaternion.identity;
        private TrackedDevicePose_t[]? poses;
        private Vector3 hmdAbsolutePosition = new();
        private Vector3 hmdVsPlayer = new();
        private Vector3 lastControllerMove = new();
        private Vector3 vrCamPosition = new(0, 1.75f, 0);
        private GameObject vrPlayer = null!;
        private GameObject SteamVRobject = null!;
        private readonly List<BoxCollider> colliders = new(5);
        private readonly HashSet<Transform> canvasses = new();
        private readonly HashSet<MonoBehaviour> UIElements = new();
        public float Deadzone = 0.0f;
        public float speed = 0.5f;
        private string fallback_fist = string.Empty;
        private string fallback_point = string.Empty;
        private string fallback_relaxed = string.Empty;
        public Transform? playerChar;
        private AssetBundle? bundle;
        private readonly bool debug = true;
        private bool InitializedSteamRVObjects = false;
        private LayerMask defaultHandMask = LayerMask.GetMask("Default", "UI", "Walls", "Ground", "Character", "Ragdolls", "InteractiveItems");
        private bool boundPlayerHands;

        private bool HandInputActive => leftHand.grabGripAction.stateDown
            || leftHand.grabPinchAction.stateDown
            || leftHand.uiInteractAction.stateDown
            || rightHand.grabGripAction.stateDown
            || rightHand.grabPinchAction.stateDown
            || rightHand.uiInteractAction.stateDown;

        private readonly List<InteractiveItem> Items = new();

        static HPVR()
        {
            //MelonLogger.Msg("Static init");
            AssemblyResolverYoinker.SetOurResolveHandlerAtFront();
            //foreach (var item in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            //{
            //    MelonLogger.Msg(item);
            //}
        }

        public HPVR()
        {
        }


        //##################################################################
        //##################################################################
        //##
        //##    Steps still left to do before release:
        //##    - Main menu UI has to be fully workable
        //##    - in game items have to work on ui click, not necessarily with physics
        //##    - dialogue in game needs to work
        //##    - bind controllers to all actions needed to play through the game, so 
        //##        - Inventory, memories and Opportunity with the Q radial
        //##        - Game Menu
        //##        - E Radial



        public override void OnInitializeMelon()
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

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", "", "vrshaders");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", ".manifest", "vrshaders");

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
                this.Unregister("VR Headset was not connected before starting the game", false);
                return;
            }
            MelonXR.Initialize();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg("[HPVR] preparing scene");
            removedPlayerHead = false;
            inGameMain = sceneName == "GameMain";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";
            SetUpSteamActionsIfNeeded();

            if (!InitializedSteamRVObjects)
            {
                //set up steamvr objects, camera and stuff
                SetUpSteamVR();
                //set up controller objects
                SetUpControllers();
                //add the hands to the player and start it
                FinalizeSteamVRSetup();
                InitializedSteamRVObjects = true;
            }
            else
            {
                Player.instance.headCollider = SetUpCamera();
            }

            if (inGameMain)
            {
                playerChar = PlayerCharacter.Player.transform;

                vrPlayer.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                vrPlayer.transform.position = new(0.65f, 0, 3.55f);

                if (inGameMain && PlayerCharacter.Player is not null)
                {
                    RemovePlayerHead();
                }

                leftHand.useHoverSphere = true;
                leftHand.useControllerHoverComponent = false;
                leftHand.useFingerJointHover = true;
                rightHand.useHoverSphere = true;
                rightHand.useControllerHoverComponent = false;
                rightHand.useFingerJointHover = true;
            }
            else if (inMainMenu)
            {
                leftHand.useHoverSphere = false;
                leftHand.useControllerHoverComponent = false;
                leftHand.useFingerJointHover = true;
                rightHand.useHoverSphere = false;
                rightHand.useControllerHoverComponent = false;
                rightHand.useFingerJointHover = true;

                vrPlayer.transform.rotation = Quaternion.Euler(0, 0, 0);
                vrPlayer.transform.position = new(0.55f, 0, -10);

                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;

                CreateMainMenuBoundary();

                QualitySettings.SetQualityLevel(1);
            }
            else
            {
                leftHand.useHoverSphere = false;
                leftHand.useControllerHoverComponent = false;
                leftHand.useFingerJointHover = false;
                rightHand.useHoverSphere = false;
                rightHand.useControllerHoverComponent = false;
                rightHand.useFingerJointHover = true;
                vrPlayer.transform.rotation = Quaternion.Euler(0, 0, 0);
            }

            if (inLoadingScreen)
            {
                var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
                cinemachineBrain.gameObject.SetActive(false);
            }

            PrepareUIforVR();
            Hand.UpdateScene();

            MelonLogger.Msg("[HPVR] scene preparation done");
        }

        private void FinalizeSteamVRSetup()
        {
            var player = vrPlayer.GetComponent<Player>();
            player.hands = new Hand[] { leftHand, rightHand };
            player.Init();
            MelonCoroutines.Start(player.Start());
        }

        private void SetUpSteamVR()
        {
            vrPlayer = new GameObject("VR Player");
            Object.DontDestroyOnLoad(vrPlayer);

            //we need a steamvr player as well for the hands :(
            SteamVRobject = new GameObject("SteamVR");
            SteamVRobject.transform.parent = vrPlayer.transform;

            var player = vrPlayer.AddComponent<Player>();
            player.trackingOriginTransform = vrPlayer.transform;
            player.hmdTransforms = new Transform[] { Camera.main.transform };
            player.audioListener = Camera.main.transform;
            player.headCollider = SetUpCamera();
            player.rigSteamVR = SteamVRobject;
            player.headsetOnHead = SteamVR_Actions.default_HeadsetOnHead;
            player.allowToggleTo2D = false;

            Object.DontDestroyOnLoad(SteamVRobject);

            var input = new GameObject("VRInputModule");
            input.transform.parent = SteamVRobject.transform;
            var eventSystem = input.AddComponent<EventSystem>();
            eventSystem.m_FirstSelected = null;
            eventSystem.sendNavigationEvents = false;
            eventSystem.m_DragThreshold = 0;
            var inputComponent = input.AddComponent<InputModule>();
            inputComponent.sendPointerHoverToParent = true;

            //seems this one is too old?
            //replaced standaloneinputmodule with inputsystemuiinputmodule
            var standalone = input.AddComponent<InputSystemUIInputModule>();
            standalone.sendPointerHoverToParent = true;
            standalone.repeatDelay = 0.5f;
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

        private void SetUpControllers()
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
            leftHand.OnHandInitialized += (int i) => { leftHand.gameObject.AddComponent<Laser>(); };
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

        private void PrepareUIforVR()
        {
            canvasses.Clear();
            UIElements.Clear();
            //only move ui which is notr already world space
            //set scale to 0.001 for all axis
            //set about 1.7 units in front of the vr cam
            foreach (var gameObject in GameObject.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
            {
                var canvas = gameObject.TryCast<Canvas>();
                if (canvas is null || gameObject.hideFlags != HideFlags.None)
                {
                    //MelonLogger.Msg(gameObject.name + " " + ((int)gameObject.hideFlags));
                    continue;
                }
                if (canvasses.Contains(canvas.transform))
                {
                    continue;
                }

                MelonLogger.Msg(canvas.name + " original Position and scale" + canvas.transform.position.ToString() + " - " + canvas.transform.localScale.ToString());

                if (canvas.transform.localScale == Vector3.zero)
                {
                    canvas.transform.localScale = Vector3.one;
                }

                switch (canvas.renderMode)
                {
                    case RenderMode.WorldSpace:
                        continue;
                    case RenderMode.ScreenSpaceCamera:
                        canvas.transform.localScale *= 0.02f;
                        break;
                    default:
                        canvas.transform.localScale *= 0.0008f;
                        break;
                }

                //todo tune
                //canvas.scaleFactor *= 1.1f;
                canvas.renderMode = RenderMode.WorldSpace;
                if (inGameMain)
                {
                    //todo only do for some types ui, namely the ones that always show and interaciton target
                    //dialogue ui
                    //stamina
                    //bgc 
                    //message bubbles
                    //big disclaimer text
                    //reticle
                    //
                    switch (canvas.gameObject.name)
                    {
                        case "DialogueCanvas":
                        case "InteractionCanvas":
                        case "BGCUICanvas":
                        case "UseSelectCanvas":
                        case "OrgasmCanvas":
                        case "NarratorCanvas":
                        case "Canvas": // should be something messages canvas
                        case " Canvas": // should be something quest canvas
                        case "Canvas ": // should be something quest canvas
                        case "RadialMenuCanvas":
                        case "DebugCanvas":
                        case "ConsoleCanvas":
                        case "Relationship Notificatiops Canvas":
                        case "UIRadialMenuCanvas":
                            canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                            break;
                        default:
                            break;
                    }
                }
                canvasses.Add(canvas.transform);
            }
            UpdateUIPositions();

            //todo sliders are found and they trigger to 0 on click, have to investigate their original unity classes
            //todo the dropdowns drop down, but the colliders behind still trigger. also the dropdowns collider is not correctly adjusted to the new size
            //todo the scrollviews block the rest with their colliders, only enable those which would be visible
            //see simplecolorpicker 
            //also in the customization environment things are offset

            SetUpUIObjectsOfType<Selectable>();
            SetUpUIObjectsOfType<Dropdown.DropdownItem>();
            SetUpUIObjectsOfType<TMP_Dropdown.DropdownItem>();
            SetUpUIObjectsOfType<PaletteGradient>();
        }

        //void SimpleColorPicker.Scripts.PaletteGradient$$OnPointerDown
        //                (SimpleColorPicker_Scripts_PaletteGradient_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)
        //{
        //    code* pcVar1;
        //    UnityEngine_Vector2_Fields screenPoint;
        //    bool isInRectangle;
        //    int32_t canvasrenderMode;
        //    UnityEngine_RectTransform_o* rect;
        //    UnityEngine_Camera_o* cam;
        //    UnityEngine_Vector2_Fields position;
        //    UnityEngine_Canvas_o* canvas;
        //    SimpleColorPicker_Scripts_ColorJoystick_o* colorJoystick;

        //    position.x = 0.0;
        //    position.y = 0.0;
        //    rect = UnityEngine.Component.GetComponent<RectTransform>());
        //    if (eventData is not null)
        //    {
        //        canvas = (__this->fields).Canvas;
        //        screenPoint = (eventData->fields)._position_k__BackingField.fields;
        //        if (canvas is not null)
        //        {
        //            canvasrenderMode = UnityEngine.Canvas$$get_renderMode(canvas, (MethodInfo*)0x0);
        //            if (canvasrenderMode == 1)
        //            {
        //                cam = UnityEngine.Camera$$get_main((MethodInfo*)0x0);
        //            }
        //            else
        //            {
        //                cam = (UnityEngine_Camera_o*)0x0;
        //            }
        //            
        //            isInRectangle =
        //                    UnityEngine.RectTransformUtility$$ScreenPointToLocalPointInRectangle
        //                            (rect, (UnityEngine_Vector2_o)screenPoint, cam, (UnityEngine_Vector2_o*)&position
        //                            , (MethodInfo*)0x0);
        //            if (isInRectangle)
        //            {
        //                colorJoystick = (__this->fields).ColorJoystick;
        //                if (colorJoystick is null)
        //                    goto LAB_1806cc252;
        //                SimpleColorPicker.Scripts.ColorJoystick$$OnDrag(colorJoystick, eventData, (MethodInfo*)0x0);
        //            }
        //            return;
        //        }
        //    }
        //LAB_1806cc252:
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //void SimpleColorPicker.Scripts.PaletteGradient$$OnDrag
        //                (SimpleColorPicker_Scripts_PaletteGradient_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)
        //{
        //    code* pcVar1;
        //    SimpleColorPicker_Scripts_ColorJoystick_o* colorJoystick;

        //    colorJoystick = (__this->fields).ColorJoystick;
        //    if (colorJoystick is not null)
        //    {
        //        SimpleColorPicker.Scripts.ColorJoystick$$OnDrag(colorJoystick, eventData, (MethodInfo*)0x0);
        //        return;
        //    }
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //void SimpleColorPicker.Scripts.ColorJoystick$$OnDrag

        //                (SimpleColorPicker_Scripts_ColorJoystick_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)

        //{
        //    code* pcVar1;
        //    UnityEngine_Vector2_Fields screenPoint;
        //    undefined8 uVar2;
        //    undefined8 uVar3;
        //    bool inRectPlane;
        //    int32_t canvasRenderMode;
        //    int iVar4;
        //    int iVar5;
        //    int iVar6;
        //    int iVar7;
        //    UnityEngine_Camera_o* cam;
        //    UnityEngine_Rect_o* Rect;
        //    UnityEngine_Transform_o* transform;
        //    UnityEngine_Color_o* color;
        //    float H;
        //    float SumMin;
        //    UnityEngine_Vector2_o eventPosition;
        //    UnityEngine_Rect_o Newrect[6];
        //    UnityEngine_Canvas_o* Canvas;
        //    SimpleColorPicker_Scripts_ColorPicker_o* ColorPickerScript;
        //    SimpleColorPicker_Scripts_ColorSlider_o* ColorSliderScript;
        //    UnityEngine_UI_Slider_o* ColorSliderSlider;
        //    UnityEngine_Texture2D_o* Tex2D;
        //    float eventX;
        //    float floatVal;
        //    UnityEngine_RectTransform_o* rectTransform;
        //    float rectheight;

        //    if (DAT_183a97ecd == '\0')
        //    {
        //        thunk_FUN_1803888b0(&UnityEngine.RectTransformUtility_TypeInfo);
        //        DAT_183a97ecd = '\x01';
        //    }
        //    rectTransform = (__this->fields).RectTransform;
        //    eventPosition.fields.x = 0.0;
        //    eventPosition.fields.y = 0.0;
        //    if (eventData != (UnityEngine_EventSystems_PointerEventData_o*)0x0)
        //    {
        //        Canvas = (__this->fields).Canvas;
        //        screenPoint = (eventData->fields)._position_k__BackingField.fields;
        //        if (Canvas != (UnityEngine_Canvas_o*)0x0)
        //        {
        //            canvasRenderMode = UnityEngine.Canvas$$get_renderMode(Canvas, (MethodInfo*)0x0);
        //            if (canvasRenderMode == 1)
        //            {
        //                cam = UnityEngine.Camera$$get_main((MethodInfo*)0x0);
        //            }
        //            else
        //            {
        //                cam = (UnityEngine_Camera_o*)0x0;
        //            }
        //            if ((UnityEngine.RectTransformUtility_TypeInfo->_2).cctor_finished == 0)
        //            {
        //                il2cpp_runtime_class_init();
        //            }
        //            inRectPlane = UnityEngine.RectTransformUtility$$ScreenPointToLocalPointInRectangle
        //                                    (rectTransform, (UnityEngine_Vector2_o)screenPoint, cam, &eventPosition,
        //                                        (MethodInfo*)0x0);
        //            floatVal = eventPosition.fields.x;
        //            if (!inRectPlane)
        //            {
        //                return;
        //            }
        //            rectTransform = (__this->fields).RectTransform;
        //            if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //            {
        //                Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                SumMin = eventPosition.fields.y;
        //                eventX = (Rect->fields).m_XMin;
        //                eventPosition.fields.x = floatVal;
        //                if (floatVal <= eventX)
        //                {
        //                    eventPosition.fields.x = eventX;
        //                }
        //                rectTransform = (__this->fields).RectTransform;
        //                if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                {
        //                    Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                    eventX = eventPosition.fields.x;
        //                    floatVal = (Rect->fields).m_YMin;
        //                    eventPosition.fields.y = SumMin;
        //                    if (SumMin <= floatVal)
        //                    {
        //                        eventPosition.fields.y = floatVal;
        //                    }
        //                    rectTransform = (__this->fields).RectTransform;
        //                    if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                    {
        //                        Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                        floatVal = eventPosition.fields.y;
        //                        SumMin = (Rect->fields).m_Width + (Rect->fields).m_XMin;
        //                        eventPosition.fields.x = eventX;
        //                        if (SumMin <= eventX)
        //                        {
        //                            eventPosition.fields.x = SumMin;
        //                        }
        //                        rectTransform = (__this->fields).RectTransform;
        //                        if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                        {
        //                            Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                            eventX = (Rect->fields).m_Height + (Rect->fields).m_YMin;
        //                            eventPosition.fields.y = floatVal;
        //                            if (eventX <= floatVal)
        //                            {
        //                                eventPosition.fields.y = eventX;
        //                            }
        //                            transform = UnityEngine.Component$$get_transform
        //                                                    ((UnityEngine_Component_o*)__this, (MethodInfo*)0x0);
        //                            if (transform != (UnityEngine_Transform_o*)0x0)
        //                            {
        //                                Newrect[0].fields.m_YMin = eventPosition.fields.y;
        //                                Newrect[0].fields.m_XMin = eventPosition.fields.x;
        //                                Newrect[0].fields.m_Width = 0.0;
        //                                UnityEngine.Transform$$set_localPosition
        //                                            (transform, (UnityEngine_Vector3_o*)Newrect, (MethodInfo*)0x0);
        //                                floatVal = eventPosition.fields.x;
        //                                ColorPickerScript = (__this->fields).ColorPicker;
        //                                if (ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                {
        //                                    rectTransform = (__this->fields).RectTransform;
        //                                    Tex2D = (ColorPickerScript->fields).Texture;
        //                                    if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                                    {
        //                                        Rect = UnityEngine.RectTransform$$get_rect
        //                                                            (Newrect, rectTransform, (MethodInfo*)0x0);
        //                                        eventX = (Rect->fields).m_Width;
        //                                        if (Tex2D != (UnityEngine_Texture2D_o*)0x0)
        //                                        {
        //                                            iVar4 = (*(Tex2D->klass->vtable)._5_get_width.methodPtr)
        //                                                                (Tex2D, (Tex2D->klass->vtable)._5_get_width.method);
        //                                            SumMin = eventPosition.fields.y;
        //                                            rectTransform = (__this->fields).RectTransform;
        //                                            if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                                            {
        //                                                Rect = UnityEngine.RectTransform$$get_rect
        //                                                                    (Newrect, rectTransform, (MethodInfo*)0x0);
        //                                                rectheight = (Rect->fields).m_Height;
        //                                                iVar5 = (*(Tex2D->klass->vtable)._7_get_height.methodPtr)
        //                                                                    (Tex2D, (Tex2D->klass->vtable)._7_get_height.method);
        //                                                ColorPickerScript = (__this->fields).ColorPicker;
        //                                                if (((ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                    && (ColorSliderScript = (ColorPickerScript->fields).H,
        //                                                        ColorSliderScript != (SimpleColorPicker_Scripts_ColorSlider_o*)0x0))
        //                                                    && (ColorSliderSlider = (ColorSliderScript->fields).Slider,
        //                                                        ColorSliderSlider != (UnityEngine_UI_Slider_o*)0x0))
        //                                                {
        //                                                    H = (float)(*(ColorSliderSlider->klass->vtable)._46_get_value.methodPtr)
        //                                                                            (ColorSliderSlider,
        //                                                                            (ColorSliderSlider->klass->vtable)._46_get_value.
        //                                                                            method);
        //                                                    iVar6 = (*(Tex2D->klass->vtable)._5_get_width.methodPtr)
        //                                                                        (Tex2D, (Tex2D->klass->vtable)._5_get_width.method);
        //                                                    iVar7 = (*(Tex2D->klass->vtable)._7_get_height.methodPtr)
        //                                                                        (Tex2D, (Tex2D->klass->vtable)._7_get_height.method);
        //                                                    color = UnityEngine.Color$$HSVToRGB
        //                                                                        ((UnityEngine_Color_o*)Newrect, H,
        //                                                                        ((floatVal / eventX) * (float)iVar4) / (float)iVar6,
        //                                                                        ((SumMin / rectheight) * (float)iVar5) / (float)iVar7,
        //                                                                        true, (MethodInfo*)0x0);
        //                                                    uVar3._0_4_ = (color->fields).r;
        //                                                    uVar3._4_4_ = (color->fields).g;
        //                                                    uVar2._0_4_ = (color->fields).r;
        //                                                    uVar2._4_4_ = (color->fields).g;
        //                                                    floatVal = (color->fields).b;
        //                                                    ColorPickerScript = (__this->fields).ColorPicker;
        //                                                    if (((ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                        && (ColorSliderScript = (ColorPickerScript->fields).A,
        //                                                            ColorSliderScript != (SimpleColorPicker_Scripts_ColorSlider_o*)0x0
        //                                                            )) && (ColorSliderSlider = (ColorSliderScript->fields).Slider,
        //                                                                    ColorSliderSlider != (UnityEngine_UI_Slider_o*)0x0))
        //                                                    {
        //                                                        Newrect[0].fields.m_Height =
        //                                                                (float)(*(ColorSliderSlider->klass->vtable)._46_get_value.methodPtr
        //                                                                    )(ColorSliderSlider,
        //                                                                        (ColorSliderSlider->klass->vtable)._46_get_value.method);
        //                                                        ColorPickerScript = (__this->fields).ColorPicker;
        //                                                        Newrect[0].fields._0_8_ = uVar2;
        //                                                        Newrect[0].fields.m_Width = floatVal;
        //                                                        if (ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                        {
        //                                                            Newrect[0].fields._0_8_ = uVar3;
        //                                                            SimpleColorPicker.Scripts.ColorPicker$$SetColor
        //                                                                        (ColorPickerScript, (UnityEngine_Color_o*)Newrect, false, true
        //                                                                        , true, true, (MethodInfo*)0x0);
        //                                                            return;
        //                                                        }
        //                                                    }
        //                                                }
        //                                            }
        //                                        }
        //                                    }
        //                                }
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //todo for the color picker it seems we just want to call the colorjoysticks ondrag ourselfs
        //if the position is right it should be fine, seems we send the wrong coordinates
        //we already have to translate the hit coords into the local points for the object beforehand

        private void SetUpUIObjectsOfType<T>() where T : MonoBehaviour
        {
            foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
            {
                var uiComponent = obj.TryCast<T>();
                if (uiComponent?.gameObject?.hideFlags != HideFlags.None)
                { continue; }

                if (UIElements.Contains(uiComponent))
                { continue; }

                UIElements.Add(uiComponent);

                var inter = uiComponent.gameObject.AddComponent<Interactable>();
                inter.highlightOnHover = false;
                inter.handFollowTransform = false;
                inter.snapAttachEaseInTime = 0.15f;
                inter.useHandObjectAttachmentPoint = false;

                uiComponent.gameObject.AddComponent<UIElement>();
            }
        }

        private void UpdateUIPositions()
        {
            if (inMainMenu)
            {
                foreach (var canvas in canvasses)
                {
                    canvas.position = new(1, 1.4f, -8.3f);
                }
            }
            else
            {
                foreach (var canvas in canvasses)
                {
                    if (!canvas.gameObject.active)
                    {
                        continue;
                    }

                    if (inGameMain && canvas.gameObject.name == "InteractionCanvas")
                    {
                        if (Laser.LastHit != Vector3.zero)
                        {
                            canvas.position = Laser.LastHit + (vrCamRotation * Vector3.forward * -0.05f);
                        }
                        else
                        {
                            canvas.position = vrCamPosition + (vrCamRotation * Vector3.forward * 1.45f);
                        }
                    }
                    else
                    {
                        canvas.position = vrCamPosition + (vrCamRotation * Vector3.forward * 1.5f);
                    }
                    canvas.rotation = vrCamRotation;
                }
            }
        }

        private void CreateMainMenuBoundary()
        {
            colliders.Clear();
            Material m = new(GameObject.Find("Floor").GetComponent<MeshRenderer>().material);

            //set up colliders around the menu area so we cannot fall off
            var border1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border1.layer = LayerMask.NameToLayer("Walls");
            border1.GetComponent<MeshRenderer>().material = m;
            border1.GetComponent<BoxCollider>().size = new Vector3(0.5f, 12, 20);
            border1.transform.localScale = new Vector3(0.5f, 12, 20);
            border1.transform.position = new Vector3(5, 5.5f, -8);
            border1.name = "HPVR collider right";
            var border2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border2.layer = LayerMask.NameToLayer("Walls");
            border2.GetComponent<MeshRenderer>().material = m;
            border2.GetComponent<BoxCollider>().size = new Vector3(0.5f, 12, 20);
            border2.transform.localScale = new Vector3(0.5f, 12, 20);
            border2.transform.position = new Vector3(-6, 5.5f, -8);
            border2.name = "HPVR collider left";
            var border3 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border3.layer = LayerMask.NameToLayer("Walls");
            border3.GetComponent<MeshRenderer>().material = m;
            border3.GetComponent<BoxCollider>().size = new Vector3(12, 12, 0.5f);
            border3.transform.localScale = new Vector3(12, 12, 0.5f);
            border3.transform.position = new Vector3(0, 5.5f, -14.5f);
            border3.name = "HPVR collider back";
            var border4 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border4.layer = LayerMask.NameToLayer("Walls");
            border4.GetComponent<MeshRenderer>().material = m;
            border4.GetComponent<BoxCollider>().size = new Vector3(12, 12, 0.5f);
            border4.transform.localScale = new Vector3(12, 12, 0.5f);
            border4.transform.position = new Vector3(0, 5.5f, -1);
            border4.name = "HPVR collider front";
            var border5 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border5.layer = LayerMask.NameToLayer("Walls");
            border5.GetComponent<MeshRenderer>().material = m;
            border5.GetComponent<BoxCollider>().size = new Vector3(14, 0.5f, 20);
            border5.transform.localScale = new Vector3(14, 0.5f, 20);
            border5.transform.position = new Vector3(-0.5f, 11, -8);
            border5.name = "HPVR collider top";
            var Container = new GameObject("HPVR Collider container");
            border1.transform.parent = Container.transform;
            border2.transform.parent = Container.transform;
            border3.transform.parent = Container.transform;
            border4.transform.parent = Container.transform;
            border5.transform.parent = Container.transform;
            colliders.Add(border1.GetComponent<BoxCollider>());
            colliders.Add(border2.GetComponent<BoxCollider>());
            colliders.Add(border3.GetComponent<BoxCollider>());
            colliders.Add(border4.GetComponent<BoxCollider>());
        }

        private void SetUpSteamActionsIfNeeded()
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
                if (inGameMain || inMainMenu)
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
                }
            };

            SteamVR_Actions.default_SnapTurnLeft.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {
                if (inGameMain || inMainMenu)
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
                    //UpdateHMDPositions();
                }
            };
            SteamVR_Actions.default_SnapTurnRight.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {
                if (inGameMain || inMainMenu)
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
                    //UpdateHMDPositions();
                }
            };

            SetUpInput = true;
            MelonLogger.Msg("Activated SteamVR actions");
        }

        //todos:
        //remove cinemachinebrain during cutscenes and loading screen (like with third person camera)
        //player collissions check ignore hands somehow plss?
        //ui interaction?
        //bind controllers
        //put interactable script on everything with interactive item
        //player hands have a monobehaviour handposer on them. might need to remove for vr
        //player hand ik bind to gloves
        //maybe do IK with the player object -> finalik dokumentation
        //curve ui canvases slightly
        //build a keyboard? using maybe Ikeyboardevent

        public override void OnUpdate()
        {
            if (SteamVR_Camera.instance?.transform is null)
            {
                return;
            }

            HandleControllerMovement();

            if (inGameMain)
            {
                if (!removedPlayerHead)
                {
                    RemovePlayerHead();
                }
                if (!boundPlayerHands)
                {
                    BindPlayerHandsToVRHands();
                }

                UpdateInteractiveItems();

                ScalePlayerToHMDHeight();
            }
            else if (inLoadingScreen)
            {
                TryEndLoadingScreen();
            }
            else if (inDisclaimer)
            {
                TryEndDisclaimerScreen();
            }

            if (!inMainMenu)
            {
                //dont update position in menu as we have to do some very fine controls and not just answer stuff
                UpdateUIPositions();
            }

            UpdateHMDPositions();
        }

        private void TryEndDisclaimerScreen()
        {
            var disclaimer = Object.FindObjectOfType<DisclaimerManager>();
            if (!disclaimer._shouldProcessSceneTransition && !disclaimer._loadedNextScene && HandInputActive)
            {
                disclaimer._shouldProcessSceneTransition = true;
            }
        }

        private void TryEndLoadingScreen()
        {
            var loading = Object.FindObjectOfType<LoadingScreenManager>();
            if (loading is null)
            {
                return;
            }
            if (loading._gameLoader is null)
            {
                return;
            }
            if (!loading._gameLoader.allowSceneActivation && loading._loaded && loading._gameLoader.progress >= 0.9f && HandInputActive)
            {
                loading._gameLoader.allowSceneActivation = true;
            }
        }

        private void UpdateInteractiveItems()
        {
            if (ItemManager.Singleton is null)
            {
                return;
            }

            if (ItemManager.Singleton.Items.Count == Items.Count)
            {
                return;
            }

            foreach (var item in ItemManager.Singleton.Items)
            {
                if (item is null || item.gameObject is null)
                {
                    continue;
                }
                if (!Items.Contains(item))
                {
                    Items.Add(item);
                    if (item.SpecialItemType == Il2CppEekEvents.Items.SpecialItemTypes.None)
                    {
                        item.gameObject.AddComponent<VelocityEstimator>();
                        var inter = item.gameObject.AddComponent<Interactable>();
                        inter.highlightOnHover = false;
                        inter.handFollowTransform = true;
                        inter.snapAttachEaseInTime = 0.15f;
                        inter.useHandObjectAttachmentPoint = true;
                        if (item.gameObject.GetComponent<Rigidbody>() is not null)
                        {
                            var thrower = item.gameObject.AddComponent<Throwable>();
                            thrower.attachmentFlags = Hand.AttachmentFlags.SnapOnAttach | Hand.AttachmentFlags.DetachFromOtherHand | Hand.AttachmentFlags.TurnOffGravity | Hand.AttachmentFlags.VelocityMovement;
                            thrower.catchingSpeedThreshold = -1;
                            thrower.releaseVelocityStyle = ReleaseStyle.ShortEstimation;
                            thrower.releaseVelocityTimeOffset = -0.011f;
                            thrower.scaleReleaseVelocity = 1.1f;
                            thrower.scaleReleaseVelocityThreshold = -1;
                            thrower.scaleReleaseVelocityCurve = AnimationCurve.EaseInOut(0, 0.1f, 1, 1);
                            thrower.restoreOriginalParent = false;
                        }
                        item.gameObject.AddComponent<ItemInteractable>();
                        //todo add handposer depending on the type of collider we find/what object it really is
                        //todo not only mount an interactive item to the hand but keep it relative to where the hand was when grabbing
                    }
                    else
                    {
                        var inter = item.gameObject.AddComponent<Interactable>();
                        inter.highlightOnHover = false;
                        inter.useHandObjectAttachmentPoint = false;
                    }
                }
            }
        }

        private void BindPlayerHandsToVRHands()
        {
            if (PlayerCharacter.Player is null || leftController is null || rightController is null)
            {
                return;
            }

            PlayerCharacter.Player.FinalIK.BodyIK.solver.leftHandEffector.target = leftController.transform;
            PlayerCharacter.Player.FinalIK.BodyIK.solver.rightHandEffector.target = rightController.transform;

            boundPlayerHands = true;
        }

        private void HandleControllerMovement()
        {
            if (inGameMain && playerChar is not null)
            {
                Transform cameraTransform = SteamVR_Camera.instance.transform;
                playerChar.rotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);

                hmdVsPlayer = new Vector3(cameraTransform.position.x - playerChar.position.x, 0, cameraTransform.position.z - playerChar.position.z) + lastControllerMove/* + ((playerChar.rotation * Vector3.back) * 0.1f)*/;

                colliding = (((int)PlayerCharacter.Player.Controller.Move_Injected(ref hmdVsPlayer)) & 1) == 1;
            }
            else
            {
                colliding = false;
            }

            if (inMainMenu)
            {
                foreach (var collider in colliders)
                {
                    colliding = collider.bounds.Contains(vrCamPosition + lastControllerMove);
                    if (colliding)
                    {
                        break;
                    }
                }
            }

            if (!colliding)
            {
                if (inGameMain)
                {
                    if (!PlayerCharacter.Player.IsImmobile && playerChar is not null)
                    {
                        vrPlayer.transform.position += new Vector3(lastControllerMove.x, 0, lastControllerMove.z);
                        vrPlayer.transform.position = new(vrPlayer.transform.position.x, playerChar.position.y, vrPlayer.transform.position.z);
                    }
                }
                else
                {
                    vrPlayer.transform.position += new Vector3(lastControllerMove.x, 0, lastControllerMove.z);
                }
            }

            lastControllerMove = Vector3.zero;
        }

        private void ScalePlayerToHMDHeight()
        {
            //MelonLogger.Msg(hmdAbsolutePosition.y);
            if (hmdAbsolutePosition.y > 1f)
            {
                if (PlayerCharacter.Player.Gender == Genders.Male)
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(hmdAbsolutePosition.y / 1.75f);
                }
                else
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(hmdAbsolutePosition.y / 1.65f);
                }
                PlayerCharacter.Player.IsCrouching = false;
            }
            else
            {
                if (PlayerCharacter.Player.Gender == Genders.Male)
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(hmdAbsolutePosition.y / 0.8f);
                }
                else
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(hmdAbsolutePosition.y / 0.73f);
                }
                PlayerCharacter.Player.IsCrouching = true;
            }
        }

        private void RemovePlayerHead()
        {
            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                cam.cullingMask &= ~LayerMask.GetMask("InvisibleToMainCamera");
            }

            //todo somehow the cullmask is still wrong
            if (PlayerCharacter.Player.Gender == Genders.Female)
            {
                GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
            }
            else
            {
                GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
            }
            GameObject.Find("Hair_Mirror")?.SetActive(false);
            var lEye = playerChar.FindDeepChild("lEye");
            lEye.localScale = Vector3.zero;
            var rEye = playerChar.FindDeepChild("rEye");
            rEye.localScale = Vector3.zero;
            removedPlayerHead = true;
        }

        private void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow();
            poses = new TrackedDevicePose_t[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            hmdAbsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            //todo something is still off here, rotating translates the player a little. feels like rotating around where the headset started
            vrCamRotation = vrPlayer.transform.rotation * poses[0].mDeviceToAbsoluteTracking.GetRotation();
            vrCamPosition = vrPlayer.transform.position + (vrPlayer.transform.rotation * hmdAbsolutePosition);

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
    }
}