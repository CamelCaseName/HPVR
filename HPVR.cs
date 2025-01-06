using Harmony;
using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents;
using Il2CppEekEvents.Content;
using Il2CppEekEvents.Helper;
using Il2CppEekUI;
using Il2CppHouseParty;
using Il2CppInterop.Runtime;
using Il2CppSystem.Runtime.CompilerServices;
using MelonLoader;
using SteamXR_Melon;
using System.Reflection;
using System.Runtime.Loader;
using UnityEngine;
using UnityEngine.Networking;
using Valve.VR;
using Valve.VR.InteractionSystem;
using YamlDotNet.Serialization;
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
        private Quaternion vrCamRotation = Quaternion.identity;
        private Quaternion vrControllerRotation = Quaternion.identity;
        private TrackedDevicePose_t[]? poses;
        private Vector3 hmdAbsolutePosition = new();
        private Vector3 hmdVsPlayer = new();
        private Vector3 lastControllerMove = new();
        private Vector3 vrCamPosition = new(0, 1.75f, 0);
        private Vector3 vrCamPositionStart = new();
        private Vector3 vrControllerPosition = new();
        private readonly List<BoxCollider> colliders = new(5);
        private readonly List<Transform> canvasses = new();
        public float Deadzone = 0.0f;
        public float speed = 0.5f;
        private string fallback_fist = string.Empty;
        private string fallback_point = string.Empty;
        private string fallback_relaxed = string.Empty;
        public Transform? player;
        private AssetBundle? bundle;
        private readonly bool debug = true;
        private LayerMask defaultHandMask = LayerMask.GetMask("Default", "UI", "Walls", "Ground");

        #region dirtyStuff

        static HPVR()
        {
            //MelonLogger.Msg("Static init");
            SetOurResolveHandlerAtFront();
            //foreach (var item in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            //{
            //    MelonLogger.Msg(item);
            //}
        }

        private static Assembly AssemblyResolveEventListener(object sender, ResolveEventArgs args)
        {
            if (args is null)
            {
                return null!;
            }
            string cleanName = args.Name[..args.Name.IndexOf(',')];
            string name = "HPVR.Resources." + cleanName + ".dll";
            //MelonLogger.Msg(cleanName + " -> " + name);
            using Stream? str = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (str is not null)
            {
                var context = new AssemblyLoadContext(name, false);
                string path = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "UserLibs", cleanName + ".dll");
                FileStream fstr = new(path, FileMode.Create);
                str.CopyTo(fstr);
                fstr.Close();
                str.Position = 0;

                return context.LoadFromStream(str);
            }
            return null!;
        }

        private static void SetOurResolveHandlerAtFront()
        {
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            FieldInfo? field = null;

            Type domainType = typeof(AssemblyLoadContext);

            while (field is null)
            {
                if (domainType is not null)
                {
                    field = domainType.GetField("AssemblyResolve", flags);
                }
                else
                {
                    MelonLogger.Error("domainType got set to null for the AssemblyResolve event was null");
                    return;
                }
                if (field is null)
                {
                    domainType = domainType.BaseType!;
                }
            }

            MulticastDelegate resolveDelegate = (MulticastDelegate)field.GetValue(null)!;
            Delegate[] subscribers = resolveDelegate.GetInvocationList();

            Delegate currentDelegate = resolveDelegate;
            for (int i = 0; i < subscribers.Length; i++)
            {
                currentDelegate = Delegate.RemoveAll(currentDelegate, subscribers[i])!;
            }

            Delegate[] newSubscriptions = new Delegate[subscribers.Length + 1];
            newSubscriptions[0] = (ResolveEventHandler)AssemblyResolveEventListener!;
            Array.Copy(subscribers, 0, newSubscriptions, 1, subscribers.Length);

            currentDelegate = Delegate.Combine(newSubscriptions)!;

            field.SetValue(null, currentDelegate);

            //MelonLogger.Msg("Set our resolve handler at the front");
        }
        #endregion

        public HPVR()
        {
        }

        public override void OnInitializeMelon()
        {
            poses = new TrackedDevicePose_t[4];
            CreateAndSavePlugin("openvr_api");
            CreateAndSavePlugin("XRSDKOpenVR");
            CreateAndSavePlugin("ucrtbased");

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
                        CreateAndSaveToPath(folderPath, "actions." + name, ".json", name);
                    }
                }
            }

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets");
            CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", "", "vrshaders");
            CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", ".manifest", "vrshaders");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "UnitySubsystems", "XRSDKOpenVR");
            CreateAndSaveToPath(folderPath, "UnitySubsystemsManifest", ".json");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "SteamVR");
            CreateAndSaveToPath(folderPath, "OpenVRSettings", ".asset");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "HPVR");
            CreateAndSaveToPath(folderPath, "assets.hpvr_assets", ".manifest", "hpvr_assets");
            fallback_fist = CreateAndSaveToPath(folderPath, "assets.fallback_fist", ".asset", "fallback_fist");
            fallback_point = CreateAndSaveToPath(folderPath, "assets.fallback_point", ".asset", "fallback_point");
            fallback_relaxed = CreateAndSaveToPath(folderPath, "assets.fallback_relaxed", ".asset", "fallback_relaxed");
            var assets = CreateAndSaveToPath(folderPath, "assets.hpvr_assets", "", "hpvr_assets");
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
            playerEye = null;
            inGameMain = sceneName == "GameMain";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";
            SetUpSteamActionsIfNeeded();

            if (inGameMain)
            {
                player = PlayerCharacter.Player.transform;

                vrControllerRotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                vrControllerPosition = new(0.7f, 0, 3.55f);
            }
            else
            {
                vrControllerRotation = Quaternion.Euler(0, 0, 0);
                vrControllerPosition = new(0.55f, 0.6635f, -10);
            }
            if (inMainMenu)
            {
                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;

                CreateMainMenuBoundary();
            }

            MoveUIToWorldSpace();

            SetHMDStartPos();

            MelonLogger.Msg("[HPVR] HPVR loading");
            MelonLogger.Msg("[HPVR] adding steamvr");
            Camera.main.gameObject.AddComponent<SteamVR_Camera>();

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                Object.DestroyImmediate(eekCam);
            }

            if (inGameMain && PlayerCharacter.Player is not null)
            {
                RemovePlayerHead();
            }

            if (inGameMain || inMainMenu)
            {
                //set up controller objects
                //todo fix
                SetUpControllers();
            }

            MelonLogger.Msg("[HPVR] HPVR loaded");
            var res = SteamVR_Camera.GetSceneResolution();
            MelonLogger.Msg($"[HPVR] Resolution: {res.width}:{res.height}");
        }

        private void SetUpControllers()
        {
            if (bundle is null)
            {
                MelonLogger.Warning("Assetbundle is null");
                return;
            }

            //todo use the prefabs but we have to rebuild all behaviours manually :(
            //so load the prefab it wants
            //instantiate
            //add all components onto it and populate them
            //send it to the steamvr components that wanted teh prefab, and mod them so they dont instantiate but just use what you give them
            leftController = new GameObject("Controller (left)");
            leftController.transform.position = new(0.25f, 1, 0);
            leftController.layer = LayerMask.NameToLayer("Character");
            var hoverSphere = new GameObject("HoverPoint");
            var objectAttachement = new GameObject("ObjectAttachement");
            hoverSphere.transform.position = new(0.052f, -0.016f, -0.1163f);
            hoverSphere.transform.parent = leftController.transform;
            objectAttachement.transform.rotation = Quaternion.Euler(135, -170, -90);
            objectAttachement.transform.position = new(0.052f, -0.0157f, -0.1163f);
            objectAttachement.transform.parent = leftController.transform;

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

            MakeBehaviour(fallback_relaxed, out SteamVR_Skeleton_Pose? fallback_relaxed_asset);
            MakeBehaviour(fallback_fist, out SteamVR_Skeleton_Pose? fallback_fist_asset);
            MakeBehaviour(fallback_point, out SteamVR_Skeleton_Pose? fallback_point_asset);
            vrLeftFallback.skeletonMainPose = fallback_relaxed_asset;
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_fist_asset);
            vrLeftFallback.skeletonAdditionalPoses.Add(fallback_point_asset);
            vrLeftFallback.Initialize();
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
            var renderModel = LeftRenderModelSlimPrefab.AddComponent<RenderModel>();
            renderModel.controllerPrefab = controllerPrefab;
            renderModel.displayControllerByDefault = false;
            renderModel.displayHandByDefault = true;
            renderModel.handPrefab = vrGloveLeftModelSlimPrefab;
            renderModel.Awake();
            MelonLogger.Warning("built the leftrendermodelslimprefab");

            var handColliderLeftPrefab = Object.Instantiate(bundle.LoadAsset("assets/steamvr/interactionsystem/core/prefabs/handcolliderleft.prefab").Cast<GameObject>());
            var handColliderLeft = handColliderLeftPrefab.AddComponent<HandCollider>();
            handColliderLeft.collisionMask = defaultHandMask;
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
            MelonLogger.Warning("built the handcolliderprefab");

            var leftHand = leftController.AddComponent<Hand>();
            leftHand.otherHand = null;
            leftHand.handType = SteamVR_Input_Sources.LeftHand;
            leftHand.trackedObject = null;
            leftHand.grabPinchAction = SteamVR_Actions.default_GrabPinch;
            leftHand.grabGripAction = SteamVR_Actions.default_GrabGrip;
            leftHand.hapticAction = SteamVR_Actions.default_Haptic;
            leftHand.uiInteractAction = SteamVR_Actions.default_InteractUI;
            leftHand.useHoverSphere = true;
            leftHand.hoverSphereTransform = hoverSphere.transform;
            leftHand.hoverSphereRadius = 0.075f;
            leftHand.hoverLayerMask = defaultHandMask;
            leftHand.hoverUpdateInterval = 0.5f;
            leftHand.useControllerHoverComponent = true;
            leftHand.controllerHoverComponent = "tip";
            leftHand.controllerHoverRadius = 0.15f;
            leftHand.useFingerJointHover = true;
            leftHand.fingerJointHover = SteamVR_Skeleton_JointIndexEnum.indexTip;
            leftHand.fingerJointHoverRadius = 0.05f;
            leftHand.objectAttachmentPoint = objectAttachement.transform;
            leftHand.noSteamVRFallbackCamera = null;
            leftHand.noSteamVRFallbackMaxDistanceNoItem = 10;
            leftHand.noSteamVRFallbackMaxDistanceWithItem = 0.5f;
            leftHand.renderModelPrefab = LeftRenderModelSlimPrefab;
            leftHand.spewDebugText = debug;
            leftHand.Awake();
            MelonCoroutines.Start(leftHand.Start());
            MelonLogger.Warning("built the left hand hand");

            var leftPose = leftController.AddComponent<SteamVR_Behaviour_Pose>();
            leftPose.poseAction = SteamVR_Actions.default_Pose;
            leftPose.inputSource = SteamVR_Input_Sources.LeftHand;
            leftPose.broadcastDeviceChanges = true;
            MelonLogger.Warning("built the left hand behaviour pose");

            var leftPhysics = leftController.AddComponent<HandPhysics>();
            leftPhysics.Initialize(handColliderLeftPrefab);
            MelonLogger.Warning("built the left hand physics");
        }

        private static bool MakeBehaviour<T>(string filePath, out T? asset) where T : MonoBehaviour
        {
            asset = null;

            if (!File.Exists(filePath))
            {
                MelonLogger.Error(filePath + " does not exist");
                return false;
            }
            try
            {
                var GO = new GameObject(Path.GetFileNameWithoutExtension(filePath));
                asset = GO.AddComponent<T>();
                //var deserializer = new DeserializerBuilder().WithNodeTypeResolver(new UnityNodeTypeResolver<T>()).Build();
                var deserializer = new Deserializer();
                var assetData = deserializer.Deserialize<T>(File.ReadAllText(filePath));

                foreach (var property in typeof(T).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetMethod is null || property.SetMethod is null)
                    {
                        continue;
                    }
                    property.SetMethod.Invoke(asset, new object?[] { property.GetMethod.Invoke(assetData, Array.Empty<object?>()) });
                }
                foreach (var field in typeof(T).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
                {
                    field.SetValue(asset, field.GetValue(assetData));
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("", e);
                if (e.InnerException is not null)
                {
                    MelonLogger.Error("", e.InnerException);
                }
            }
            return asset is not null;
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
                    vrControllerPosition = vrCamPosition;
                    vrControllerRotation *= Quaternion.AngleAxis(-45, Vector3.up);
                    //UpdateHMDPositions();
                }
            };
            SteamVR_Actions.default_SnapTurnRight.onStateDown += (SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource) =>
            {
                if (inGameMain || inMainMenu)
                {
                    SetHMDStartPos();
                    vrControllerPosition = vrCamPosition;
                    vrControllerRotation *= Quaternion.AngleAxis(45, Vector3.up);
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

            if (inGameMain && player is not null)
            {
                Transform cameraTransform = SteamVR_Camera.instance.transform;
                player.rotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);

                hmdVsPlayer = new Vector3(cameraTransform.position.x - player.position.x, 0, cameraTransform.position.z - player.position.z) + lastControllerMove + ((player.rotation * Vector3.back) * 0.1f);

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
                        vrControllerPosition += lastControllerMove;
                    }
                }
                else
                {
                    vrControllerPosition += lastControllerMove;
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
            playerEye = player.FindDeepChild("lEye")?.gameObject;
            player.FindDeepChild("rEye")?.gameObject.SetActive(false);
            player.FindDeepChild("head")?.gameObject.SetActive(false);
            playerEye?.SetActive(false);
        }

        private void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow();
            poses = new TrackedDevicePose_t[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            hmdAbsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            //this is fine
            vrCamRotation = vrControllerRotation * (poses[0].mDeviceToAbsoluteTracking.GetRotation());
            //this rotates by vrcontrollerrotation around the vrcampositionstart. we want to rotate it around hmd absolute position, so the other way around
            vrCamPosition = vrControllerPosition + (vrControllerRotation * (hmdAbsolutePosition - vrCamPositionStart));

            vrCamPosition.y = hmdAbsolutePosition.y;
            if (inGameMain && player != null)
            {
                vrCamPosition.y += player.position.y;
            }

            SteamVR_Camera.instance.transform.rotation = vrCamRotation;
            SteamVR_Camera.instance.transform.position = vrCamPosition;
        }

        static void CreateAndSavePlugin(string name)
        {
            string folderPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "Mods", "HPVR_data");
            string path = Path.Combine(folderPath, name + ".dll");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folderPath);
                using Stream? str = Assembly.GetExecutingAssembly().GetManifestResourceStream("HPVR.Resources." + name + ".dll");
                if (str is not null)
                {
                    FileStream fstr = new(path, FileMode.Create);
                    str.CopyTo(fstr);
                    fstr.Close();
                }
            }
            folderPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "HouseParty_Data", "Plugins", "x86_64");
            path = Path.Combine(folderPath, name + ".dll");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folderPath);
                using (Stream? str = Assembly.GetExecutingAssembly().GetManifestResourceStream("HPVR.Resources." + name + ".dll"))
                {
                    if (str is not null)
                    {
                        FileStream fstr = new(path, FileMode.Create);
                        str.CopyTo(fstr);
                        fstr.Close();
                    }
                }
                MelonLogger.Warning($"Loaded {name} from our embedded resources, saving for next time");
                return;
            }
        }

        static string CreateAndSaveToPath(string folderPath, string name, string ending, string filename = "")
        {
            if (string.IsNullOrEmpty(filename))
            {
                filename = name;
            }

            string path = Path.Combine(folderPath, filename + ending);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folderPath);
                var fullName = "HPVR.Resources." + name + ending;
                MelonLogger.Msg("Loading " + fullName);
                using Stream? str = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullName);
                if (str is not null)
                {
                    FileStream fstr = new(path, FileMode.Create);
                    str.CopyTo(fstr);
                    fstr.Close();
                    MelonLogger.Msg("saved " + fullName + " to " + path);
                    return path;
                }
                return string.Empty;
            }
            else
            {
                return path;
            }
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