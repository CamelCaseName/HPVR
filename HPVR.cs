using HPVR.utils;
using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents;
using Il2CppEekEvents.Helper;
using Il2CppHouseParty;
using Il2CppInterop.Runtime;
using MelonLoader;
using SteamXR_Melon;
using System.Reflection;
using UnityEngine;
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
        private bool inFade = false;
        private bool SetUpInput = false;
        private GameObject? playerEye = null;
        private GameObject? leftController = null;
        private GameObject? rightController = null;
        private Hand leftHand;
        private Hand rightHand;
        private Quaternion vrCamRotation = Quaternion.identity;
        private TrackedDevicePose_t[]? poses;
        private Vector3 hmdAbsolutePosition = new();
        private Vector3 hmdVsPlayer = new();
        private Vector3 lastControllerMove = new();
        private Vector3 vrCamPosition = new(0, 1.75f, 0);
        private Vector3 vrCamPositionStart = new();
        private GameObject vrPlayer = null!;
        private GameObject SteamVRobject = null!;
        private readonly List<BoxCollider> colliders = new(5);
        private readonly List<Transform> canvasses = new();
        public float Deadzone = 0.0f;
        public float speed = 0.5f;
        private string fallback_fist = string.Empty;
        private string fallback_point = string.Empty;
        private string fallback_relaxed = string.Empty;
        public Transform? playerChar;
        private AssetBundle? bundle;
        private readonly bool debug = true;
        private bool InitializedSteamRVObjects = false;
        private LayerMask defaultHandMask = LayerMask.GetMask("Default", "UI", "Walls", "Ground");

        #region dirtyStuff

        static HPVR()
        {
            AssemblyResolverYoinker.
                        //MelonLogger.Msg("Static init");
                        SetOurResolveHandlerAtFront();
            //foreach (var item in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            //{
            //    MelonLogger.Msg(item);
            //}
        }
        #endregion

        public HPVR()
        {
        }

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
            playerEye = null;
            inGameMain = sceneName == "GameMain";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";
            SetUpSteamActionsIfNeeded();

            if (!InitializedSteamRVObjects)
            {
                InitializedSteamRVObjects = true;
                vrPlayer = new GameObject("VR Player");
                Object.DontDestroyOnLoad(vrPlayer);

                //set up steamvr objects, camera and stuff
                SetUpSteamVR();
                //set up controller objects
                SetUpControllers();
                //add the hands to the player and start it
                FinalizeSteamVRSetup();
            }
            else
            {
                Player.instance.headCollider = SetUpCamera();
            }

            if (inGameMain)
            {
                playerChar = PlayerCharacter.Player.transform;

                vrPlayer.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                vrPlayer.transform.position = new(0.7f, 0, 3.55f);

                if (inGameMain && PlayerCharacter.Player is not null)
                {
                    RemovePlayerHead();
                }
            }
            else if (inMainMenu)
            {
                vrPlayer.transform.rotation = Quaternion.Euler(0, 0, 0);
                vrPlayer.transform.position = new(0.55f, 0.6635f, -10);

                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;

                CreateMainMenuBoundary();
            }
            else
            {
                vrPlayer.transform.rotation = Quaternion.Euler(0, 0, 0);
                vrPlayer.transform.position = new(1, 1, 1);
            }

            MoveUIToWorldSpace();

            SetHMDStartPos();

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
            //we need a steamvr player as well for the hands :(
            SteamVRobject = new GameObject("SteamVR");
            SteamVRobject.transform.parent = vrPlayer.transform;

            var player = vrPlayer.AddComponent<Player>();
            player.trackingOriginTransform = player.transform;
            player.hmdTransforms = new Transform[] { Camera.main.transform };
            player.headCollider = SetUpCamera();
            player.rigSteamVR = SteamVRobject;
            player.audioListener = Camera.main.transform;
            player.headsetOnHead = SteamVR_Actions.default_HeadsetOnHead;
            player.allowToggleTo2D = false;

            Object.DontDestroyOnLoad(SteamVRobject);
        }

        private static SphereCollider SetUpCamera()
        {
            Camera.main.gameObject.AddComponent<SteamVR_Camera>();
            var headCollider = Camera.main.gameObject.AddComponent<SphereCollider>();
            headCollider.radius = 0.12f;
            headCollider.isTrigger = false;
            headCollider.providesContacts = false;

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                Object.DestroyImmediate(eekCam);
            }

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
            leftController.layer = LayerMask.NameToLayer("Character");
            var leftHoverSphere = new GameObject("HoverPoint");
            var leftObjectAttachement = new GameObject("ObjectAttachement");
            leftHoverSphere.transform.position = new(0.052f, -0.016f, -0.1163f);
            leftHoverSphere.transform.parent = leftController.transform;
            leftObjectAttachement.transform.rotation = Quaternion.Euler(135, -170, -90);
            leftObjectAttachement.transform.position = new(0.052f, -0.0157f, -0.1163f);
            leftObjectAttachement.transform.parent = leftController.transform;

            //create the "prefabs"
            var controllerPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/controller.prefab").Cast<GameObject>());
            var model = controllerPrefab.AddComponent<SteamVR_RenderModel>();
            model.index = SteamVR_TrackedObject.EIndex.None;
            model.modelOverride = string.Empty;
            model.shader = null;
            model.verbose = debug;
            model.createComponents = true;
            model.updateDynamically = true;
            MelonLogger.Warning("built the controllerprefab");

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
            MelonLogger.Warning("built the vrgloveleftmodelslimprefab");

            var LeftRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/leftrendermodel slim.prefab").Cast<GameObject>());
            var leftRenderModel = LeftRenderModelSlimPrefab.AddComponent<RenderModel>();
            leftRenderModel.controllerPrefab = controllerPrefab;
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
            leftHand.useControllerHoverComponent = true;
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
            rightController.layer = LayerMask.NameToLayer("Character");
            var rightHoverSphere = new GameObject("HoverPoint");
            var rightObjectAttachement = new GameObject("ObjectAttachement");
            rightHoverSphere.transform.position = new(0.052f, -0.016f, -0.1163f);
            rightHoverSphere.transform.parent = rightController.transform;
            rightObjectAttachement.transform.rotation = Quaternion.Euler(135, -170, -90);
            rightObjectAttachement.transform.position = new(0.052f, -0.0157f, -0.1163f);
            rightObjectAttachement.transform.parent = rightController.transform;

            var vrGloverightModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/prefabs/vr_glove_right_model_slim.prefab").Cast<GameObject>());
            var vrGloverightFallback = vrGloverightModelSlimPrefab.transform.GetChild(1).gameObject;
            var vrrightFallback = vrGloverightFallback.AddComponent<SteamVR_Skeleton_Poser>();
            vrrightFallback.skeletonMainPose = fallback_relaxed_asset;
            vrrightFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrrightFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrrightFallback.Initialize();
            Object.DontDestroyOnLoad(vrGloverightModelSlimPrefab);
            MelonLogger.Warning("built the vrrightfallbackposer");

            var vrrightGloveSkeleton = vrGloverightModelSlimPrefab.AddComponent<SteamVR_Behaviour_Skeleton>();
            vrrightGloveSkeleton.skeletonAction = SteamVR_Actions.default_SkeletonRightHand;
            vrrightGloveSkeleton.inputSource = SteamVR_Input_Sources.RightHand;
            vrrightGloveSkeleton.rangeOfMotion = EVRSkeletalMotionRange.WithoutController;
            vrrightGloveSkeleton.skeletonRoot = vrGloverightModelSlimPrefab.transform.GetChild(0).GetChild(0);
            vrrightGloveSkeleton.origin = null!;
            vrrightGloveSkeleton.updatePose = true;
            vrrightGloveSkeleton.onlySetRotations = false;
            vrrightGloveSkeleton.skeletonBlend = 1;
            vrrightGloveSkeleton.mirroring = SteamVR_Behaviour_Skeleton.MirrorType.None;
            vrrightGloveSkeleton.fallbackPoser = vrrightFallback;
            vrrightGloveSkeleton.fallbackCurlAction = SteamVR_Actions.default_Squeeze;
            vrrightGloveSkeleton.Initialize();
            MelonLogger.Warning("built the vrgloverightmodelslimprefab");

            var rightRenderModelSlimPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/rightrendermodel slim.prefab").Cast<GameObject>());
            var rightRenderModel = rightRenderModelSlimPrefab.AddComponent<RenderModel>();
            rightRenderModel.controllerPrefab = controllerPrefab;
            rightRenderModel.displayControllerByDefault = false;
            rightRenderModel.displayHandByDefault = true;
            rightRenderModel.handPrefab = vrGloverightModelSlimPrefab;
            rightRenderModel.Initialize();
            rightRenderModel.InitAction();
            Object.DontDestroyOnLoad(rightRenderModelSlimPrefab);
            MelonLogger.Warning("built the rightrendermodelslimprefab");

            var handColliderrightPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderright.prefab").Cast<GameObject>());
            var handColliderright = handColliderrightPrefab.AddComponent<HandCollider>();
            handColliderright.collisionMask = defaultHandMask;
            handColliderright.fingerColliders = new();
            handColliderright.fingerColliders.thumbColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(0);
            handColliderright.fingerColliders.indexColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(1);
            handColliderright.fingerColliders.indexColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(2);
            handColliderright.fingerColliders.indexColliders[2] = handColliderrightPrefab.transform.GetChild(1).GetChild(3);
            handColliderright.fingerColliders.middleColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(4);
            handColliderright.fingerColliders.middleColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(5);
            handColliderright.fingerColliders.middleColliders[2] = handColliderrightPrefab.transform.GetChild(1).GetChild(6);
            handColliderright.fingerColliders.ringColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(7);
            handColliderright.fingerColliders.ringColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(8);
            handColliderright.fingerColliders.pinkyColliders[0] = handColliderrightPrefab.transform.GetChild(1).GetChild(9);
            handColliderright.fingerColliders.pinkyColliders[1] = handColliderrightPrefab.transform.GetChild(1).GetChild(10);
            handColliderright.collidersInRadius = false;
            Object.DontDestroyOnLoad(handColliderright);
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
            rightHand.otherHand = null;
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
            rightHand.useControllerHoverComponent = true;
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
            rightHand.Initialize();
            rightHand.FinishInit();
            MelonCoroutines.Start(rightHand.Start());
            MelonLogger.Warning("built the right hand hand");

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

        private void MoveUIToWorldSpace()
        {
            canvasses.Clear();
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
                        canvas.transform.localScale *= 0.0009f;
                        break;
                }
                //todo tune
                //canvas.scaleFactor *= 1.1f;
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                canvasses.Add(canvas.transform);
            }
            UpdateUIPositions();
        }

        private void UpdateUIPositions()
        {
            if (inMainMenu)
            {
                foreach (var canvas in canvasses)
                {
                    canvas.position = new(1, 1.5f, -8.3f);
                }
            }
            else
            {
                foreach (var canvas in canvasses)
                {
                    if (canvas.gameObject.active)
                    {
                        if (inGameMain && canvas.gameObject.name == "InteractionCanvas")
                        {
                            if (InteractionManager.Singleton._hit.point.sqrMagnitude != 0)
                            {
                                canvas.position = InteractionManager.Singleton._hit.point + (vrCamRotation * Vector3.forward * -0.1f);
                            }
                            else
                            {
                                canvas.position = vrCamPosition + (vrCamRotation * Vector3.forward * 1.55f);
                            }
                        }
                        else
                        {
                            canvas.position = vrCamPosition + (vrCamRotation * Vector3.forward * 1.55f);
                        }
                        canvas.rotation = vrCamRotation;
                    }
                }
            }
        }

        private void UpdateUIInteraction()
        {
            //for hitreg, cast ray out of both hands, if it lands on an active ui show a beam
            //or if the controller is in the bounds send a mouse event if the respective trigger is hit
            //send mouse event to that canvas with the simulated coords, as if it were screen size
            //https://github.com/sinai-dev/UnityExplorer/blob/1e1fb0e27bff9ab0212b4e61ef1ecb38a502b290/src/Inspectors/MouseInspectors/UiInspector.cs#L80
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

        //todo remove cinemachinebrain during cutscenes and loading screen (like with third person camera)
        //todo curve ui canvases slightly
        //todo between stand and crouch move the root player transform towards the ground so its legs bend?
        //maybe do IK with the player object -> finalik dokumentation

        private void SetHMDStartPos()
        {
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
            if (poses is null)
            {
                return;
            }
            hmdAbsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();
            vrCamPositionStart = new Vector3(hmdAbsolutePosition.x, 0, hmdAbsolutePosition.z);
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
                    SetHMDStartPos();
                    vrPlayer.transform.position = vrCamPosition;
                    vrPlayer.transform.rotation *= Quaternion.AngleAxis(-45, Vector3.up);
                    //UpdateHMDPositions();
                }
            };
            SteamVR_Actions.default_SnapTurnRight.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {
                if (inGameMain || inMainMenu)
                {
                    SetHMDStartPos();
                    vrPlayer.transform.position = vrCamPosition;
                    vrPlayer.transform.rotation *= Quaternion.AngleAxis(45, Vector3.up);
                    //UpdateHMDPositions();
                }
            };

            SetUpInput = true;
            MelonLogger.Msg("Activated SteamVR actions");
        }

        public override void OnUpdate()
        {
            if (SteamVR_Camera.instance?.transform is null)
            {
                return;
            }

            UpdateHMDPositions();

            if (inGameMain && playerChar is not null)
            {
                Transform cameraTransform = SteamVR_Camera.instance.transform;
                playerChar.rotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);

                hmdVsPlayer = new Vector3(cameraTransform.position.x - playerChar.position.x, 0, cameraTransform.position.z - playerChar.position.z) + lastControllerMove + ((playerChar.rotation * Vector3.back) * 0.1f);

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
                if (inFade)
                {
                    inFade = false;
                    SteamVR_Fade.View(Color.clear, 0.2f);
                }
                if (inGameMain)
                {
                    if (!PlayerCharacter.Player.IsImmobile)
                    {
                        vrPlayer.transform.position += lastControllerMove;
                    }
                }
                else
                {
                    vrPlayer.transform.position += lastControllerMove;
                }
            }

            if (colliding && !inFade)
            {
                inFade = true;
                SteamVR_Fade.View(Color.black, 0.2f);
            }

            lastControllerMove = Vector3.zero;

            if (inGameMain)
            {
                if (playerEye is null)
                {
                    RemovePlayerHead();
                }

                ScalePlayerToHMDHeight();
            }

            if (!inMainMenu)
            {
                //dont update position in menu as we have to do some very fine controls and not just answer stuff
                UpdateUIPositions();
            }

            if (inGameMain || inMainMenu)
            {
                UpdateUIInteraction();
            }
            else if (inLoadingScreen || inDisclaimer)
            {
                //for the loading screen and disclaimer we have to do something different
            }
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
            if (PlayerCharacter.Player.Gender == Genders.Female)
            {
                GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
            }
            else
            {
                GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
            }
            playerEye = playerChar.FindDeepChild("lEye")?.gameObject;
            playerChar.FindDeepChild("rEye")?.gameObject.SetActive(false);
            playerChar.FindDeepChild("head")?.gameObject.SetActive(false);
            playerEye?.SetActive(false);
        }

        private void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow();
            poses = new TrackedDevicePose_t[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            hmdAbsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            //this is fine
            vrCamRotation = vrPlayer.transform.rotation * (poses[0].mDeviceToAbsoluteTracking.GetRotation());
            //this rotates by vrPlayer.transform.rotation around the vrcampositionstart. we want to rotate it around hmd absolute position, so the other way around
            vrCamPosition = vrPlayer.transform.position + (vrPlayer.transform.rotation * (hmdAbsolutePosition - vrCamPositionStart));

            vrCamPosition.y = hmdAbsolutePosition.y;
            if (inGameMain && playerChar != null)
            {
                vrCamPosition.y += playerChar.position.y;
            }

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