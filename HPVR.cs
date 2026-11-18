using FidelityFX.FSR3;
using HPVR.FSR3;
using HPVR.Gameplay.Behaviours;
using HPVR.UI;
using HPVR.utils;
using HPVR.VR;
using Il2Cpp;
using Il2CppCinemachine;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Events;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents;
using Il2CppEekEvents.Helper;
using Il2CppEekUI;
using Il2CppHouseParty;
using Il2CppInterop.Runtime;
using Il2CppRootMotion.FinalIK;
using MelonLoader;
using SteamVR_Melon.Util;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR
{
    public class HPVR : MelonMod
    {
        public bool inDisclaimer;
        public bool inGameMain = false;
        public bool inLoadingScreen;
        public bool inMainMenu = false;
        public Transform? playerChar;
        private static bool shownLoadingScreenInfo = false;
        private readonly List<BoxCollider> colliders = new(5);
        readonly int II = LayerMask.NameToLayer("InteractiveItems");
        readonly int IIHighlighted = LayerMask.NameToLayer("InteractiveItemsHighlighted");
        private readonly List<InteractiveItem> Items = new();
        private Canvas? Dialogue;
        private bool DialogueVisible = false;
        private Canvas? interactionCanvas;
        private CharacterBase? DialogueSpeaker = null;
#if !VR_DISABLED
        private bool updatedCameraCull = false;
        private bool createdPlayerHandTargets;
        private Transform? LeftHandOpen;
        private Transform? RightHandOpen;
        private Transform? LeftHandRootBone;
        private Transform? RightHandRootBone;
        public VRIK? vrik;
        public Vector3 leftHandFingerRotation = new(0, -1, 60);
        public Vector3 rightHandFingerRotation = new(0, 1, -60);
        public Vector3 leftHandThumbRotation = new(-1, 10, 20);
        public Vector3 rightHandThumbRotation = new(1, 10, -20);
#endif
        private Fsr3UpscalerImageEffect? fsrScaler;
        private Fsr3UpscalerImageEffectHelper? fsrScalerHelper;
        private bool GameMainLateStarted = false;
        private bool setupVolumes;
        private MelonPreferences_Entry<Fsr3Upscaler.QualityMode>? quality;
        private bool setUpLateStartHook = false;
        private static readonly bool FSR_Enabled = false;
        private bool secondSetup = false;

        //turn into option
        private readonly int AA = 2; // 0 = none, 1 = fxaa, 2 = smaa

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
            Instance ??= this;
        }

        ~HPVR()
        {
            Enabled = false;
        }

        public static bool Enabled { get; internal set; } = true;
        public static HPVR? Instance { get; private set; }

        //& "C:\Program Files (x86)\Steam\steam.exe" steam://rungameid/611790
        ////////todo fix quest popup menu (find who populates that and then fix rotation/scale/z index there)
        ////////todo fix opportunity menu
        ////////todo fix memory menu
        ////////todo add option to menu to recalibrate player height as an entry in the menu
        ////////todo set player holding/taking item accordingly to what the player is actually grabbing
        //todo turn off cutscene movement in game main, but keep the teleporting and rotation setting in x and z
        //todo use headset movement in POV sex
        //todo use fists to hit people, depending on speed and if fully made a fist
        //todo use hand movement to masturbate
        //todo (later) screenfade can maybe just stay as an override?
        //todo (later) add teleportation, maybe steamvrs teleportation component (involved)
        //todo (later) add compatibility for headset + xbox controller

        public override void OnInitializeMelon()
        {
            try
            {
#if !VR_DISABLED
                //also turns on the unityhooks
                VRSystem.StartVR();
#else
                RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(SteamVR)));
                RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(MelonXR)));
                UnityHooks.Init();
#endif
            }
            catch (NotSupportedException ex)
            {
                MelonLogger.Msg(ex);
                this.Unregister("VR Headset was not connected before starting the game", false);
            }

            string HousePartyMainLocation = Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName;

            string folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", "", "vrshaders");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "vrshaders.vrshaders", ".manifest", "vrshaders");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "fsrshaders.fsrshaders", "", "fsrshaders");
            Il2CppHelper.CreateAndSaveToPath(folderPath, "fsrshaders.fsrshaders", ".manifest", "fsrshaders");

            var category = MelonPreferences.CreateCategory("FSR3");
            quality = category.CreateEntry("qualityMode", Fsr3Upscaler.QualityMode.Balanced, description: "Set Quality mode here: possible values: " + string.Join(", ", Enum.GetNames<Fsr3Upscaler.QualityMode>()));

            UnityHooks.EarlyUpdate += EarlyUpdate;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg("[HPVR] preparing scene " + sceneName);
            inGameMain = sceneName == "GameMain" || sceneName == "ForestEnvironment";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";
            GameMainLateStarted = false;

            if (FSR_Enabled)
            {
                DestroyFSR();
            }

#if !VR_DISABLED
            VRSystem.Gravity = inMainMenu || inGameMain;
            VRSystem.SetUpSteamVRUnity();
            UIManager.Initialize();

            updatedCameraCull = false;
#else
            MelonLogger.Msg("VR SYSTEM DISABLED IN THIS BUILD");
#endif
            //MelonLogger.Msg("Available Layers:");
            //MelonLogger.Msg(LayerMask.LayerToName(0));
            //for (int i = 0; i < 32; i++)
            //{
            //    MelonLogger.Msg(i.ToString() + ": " + LayerMask.LayerToName(i));
            //}

            Camera.main.nearClipPlane = 0.01f;

            UIManager.UpdateUIPos = true;
            shownLoadingScreenInfo = false;
            //int counter = 0;

            if (inGameMain)
            {
                if (!setUpLateStartHook)
                {
                    PlayerCharacter.add_OnPlayerLateStart(new Action(() => GameMainLateStart()));
                    setUpLateStartHook = true;
                }
#if !VR_DISABLED
                SetUpPostProcessing();
                playerChar = PlayerCharacter.Player.transform;

                Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                Player.instance.transform.position = new(0.65f, 0, 3.55f);

                Player.instance.leftHand.useHoverSphere = true;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = false;
                Player.instance.leftHand.Hide();
                Player.instance.rightHand.useHoverSphere = true;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = false;
                Player.instance.rightHand.Hide();

                Laser.LeftLaser.SetLaserStartPosition(new(), new(60, 0, 0));
                Laser.RightLaser.SetLaserStartPosition(new(), new(60, 0, 0));
#endif
            }
            else if (inMainMenu)
            {
                SetUpGraphicsSettings();
#if !VR_DISABLED

                Player.instance.leftHand.useHoverSphere = false;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = false;
                Player.instance.rightHand.useHoverSphere = false;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = false;

                VRSystem.EnableLeftCollider();
                VRSystem.EnableRightCollider();

                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);
                Player.instance.transform.position = new(0.55f, 0, -9);

                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;

                CreateMainMenuBoundary();

                UIManager.UpdateUIPos = false;
#endif
            }

#if !VR_DISABLED
            else if (Player.instance is not null)
            {
                if (Player.instance.leftHand is not null)
                {
                    Player.instance.leftHand.useHoverSphere = false;
                    Player.instance.leftHand.useControllerHoverComponent = false;
                    Player.instance.leftHand.useFingerJointHover = false;
                }
                if (Player.instance.rightHand is not null)
                {
                    Player.instance.rightHand.useHoverSphere = false;
                    Player.instance.rightHand.useControllerHoverComponent = false;
                    Player.instance.rightHand.useFingerJointHover = true;
                }
                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);

            }

            if (inLoadingScreen && Player.instance is not null)
            {
                var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
                cinemachineBrain.enabled = false;

                Player.instance.transform.position = new(0, 0, 3);
                Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);

                foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
                {
                    if (obj.name == "ScreenFade")
                    {
                        Object.DestroyImmediate(obj);
                        continue;
                    }
                    obj.Cast<Canvas>().gameObject.AddComponent<WorldSpaceOverlayUI>();
                }
            }

            UIManager.OnSceneChange();
            Hand.UpdateScene();


            if (!inGameMain && Player.instance is not null)
            {
                if (Laser.LeftLaser is not null)
                {
                    Laser.LeftLaser.LaserMask = Laser.DefaultLaserMask;
                    Laser.LeftLaser.ResetLaserStartPosition();
                }
                if (Laser.RightLaser is not null)
                {
                    Laser.RightLaser.LaserMask = Laser.DefaultLaserMask;
                    Laser.RightLaser.ResetLaserStartPosition();
                }
            }
#endif
            MelonLogger.Msg("[HPVR] scene preparation done for " + sceneName);
        }

        private void SetUpGraphicsSettings()
        {
            GraphicsController.Singleton.Toggle();
#if !VR_DISABLED
            GraphicsController.Singleton.AntiAliasing.Set(1);
            GraphicsController.Singleton.AmbientOcclusionQuality.Set(0);
            GraphicsController.Singleton.Bloom.Set(false);
            GraphicsController.Singleton.MotionBlur.Set(false);
            GraphicsController.Singleton.ScreenSpaceReflections.Set(false);
            GraphicsController.Singleton.VolumetricFogQuality.Set(0);
            GraphicsController.Singleton.ShadowQuality.Set(1);
            GraphicsController.Singleton.AntiAliasing.Set(AA);
            GraphicsController.Singleton.NVIDIADLSS.Set(0);
#endif
            fsrScaler = null;
#if VR_DISABLED
            if (HDDynamicResolutionPlatformCapabilities.DLSSDetected)
            {
                Melonlogger.Msg("enable dlss");
                GraphicsController.Singleton.NVIDIADLSS.Set(2);
            }
            else if (FSR_Enabled)
            {
                //GraphicsController.Singleton.FSRResolutionScaling.Set(3);
                MelonLogger.Msg("disabling bultin FSR");
                GraphicsController.Singleton.FSRResolutionScaling.Set(0);
                SetUpFSR3();
            }
#endif

            GraphicsController.Singleton.ApplySettings(true);
            GraphicsController.Singleton.transform.FindDeepChild("Apply").GetComponent<Button>().onClick.Invoke();
            if (GraphicsController.Singleton.IsShowing)
            {
                GraphicsController.Singleton.Toggle();
            }
        }

        private void SetUpFSR3()
        {
            //MelonLogger.Msg("Adding UNITYFSR3 Component");

            fsrScaler = Camera.main.gameObject.AddComponent<Fsr3UpscalerImageEffect>();
            fsrScaler.qualityMode = quality?.Value ?? Fsr3Upscaler.QualityMode.NativeAA;
            fsrScalerHelper = Camera.main.gameObject.AddComponent<Fsr3UpscalerImageEffectHelper>();
            fsrScaler.scene = SceneManager.GetActiveScene().name;

#if !VR_DISABLED
            SteamVRCamera.instance.ForceLast();
#endif

            //create a custom fullscreen pass on the custompass global volume
            //this gives us access to the rendercontext before the frame is pushed so we can do postprocessing
            if (!setupVolumes)
            {
                var customVolume = new GameObject("FSR_PrePostProcessVolume");
                var pass = customVolume.AddComponent<CustomPassVolume>();
                pass.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
                pass.isGlobal = true;
                pass.AddPassOfType<FsrPrePostProcess>();
                Object.DontDestroyOnLoad(customVolume);

                var customVolume2 = new GameObject("FSR_PreRefractionVolume");
                var pass2 = customVolume.AddComponent<CustomPassVolume>();
                pass2.injectionPoint = CustomPassInjectionPoint.BeforePreRefraction;
                pass2.isGlobal = true;
                pass2.AddPassOfType<FsrPreRefraction>();
                Object.DontDestroyOnLoad(customVolume2);
                setupVolumes = true;
            }

            UnityHooks.OnPreCull += fsrScalerHelper.OnPreCull;
            FsrPreRefraction.OnExecute += fsrScaler.OnPreCull;
            FsrPrePostProcess.OnExecute += fsrScaler.OnRenderImage;

            fsrScaler._helper = fsrScalerHelper;

            //MelonLogger.Msg("Added UNITYFSR3");
        }

        private void DestroyFSR()
        {
            if (fsrScalerHelper is not null)
            {
                UnityHooks.OnPreCull -= fsrScalerHelper.OnPreCull;
                Object.DestroyImmediate(fsrScalerHelper);
                fsrScalerHelper = null;
            }

            if (fsrScaler is not null)
            {
                FsrPreRefraction.OnExecute -= fsrScaler.OnPreCull;
                FsrPrePostProcess.OnExecute -= fsrScaler.OnRenderImage;
                Object.DestroyImmediate(fsrScaler);
                fsrScaler = null;
            }
        }

        private static void SetUpPostProcessing()
        {
            var volume = GameMenu.Singleton._globalVolume.GetComponent<Volume>().profile;
            foreach (var vol in volume.components)
            {
                if (vol.GetIl2CppType() == Il2CppType.Of<ScreenSpaceAmbientOcclusion>())
                {
                    vol.active = false;
                }
                else
                    if (vol.GetIl2CppType() == Il2CppType.Of<ScreenSpaceReflection>())
                    {
                        vol.active = false;
                    }
                if (vol.GetIl2CppType() == Il2CppType.Of<Bloom>())
                {
                    vol.active = false;
                }
                if (vol.GetIl2CppType() == Il2CppType.Of<ChromaticAberration>())
                {
                    vol.active = false;
                }
                if (vol.GetIl2CppType() == Il2CppType.Of<FilmGrain>())
                {
                    vol.active = false;
                }
                if (vol.GetIl2CppType() == Il2CppType.Of<MotionBlur>())
                {
                    vol.active = false;
                }
                if (vol.GetIl2CppType() == Il2CppType.Of<LensDistortion>())
                {
                    vol.active = false;
                }
            }
        }

        public override void OnUpdate()
        {
#if !VR_DISABLED
            if (SteamVRCamera.instance?.transform is null)
            {
                return;
            }

            if (inGameMain)
            {
                UpdateHPPlayerPositiion();
                if (!updatedCameraCull)
                {
                    UpdateCameraCulling();
                }

                UpdateInteractiveItems();

                if (interactionCanvas is not null)
                {
                    UpdateInteractionCanvas();
                }
                UpdateNarratorMessage();

                TryDisableMoveDuringDialogue();

                //if everythign we need for IK is enabled, go ahead and do that
                if (PlayerCharacter.Player is not null && Player.instance.leftHand is not null && Player.instance.rightHand is not null && GameMainLateStarted)
                {
                    CheckAndBindPlayerHandsToVRHands();
                }
            }
            else if (inLoadingScreen)
            {
                TryEndLoadingScreen();
                //MelonLogger.Msg($"{Player.instance.transform.position.x} {Player.instance.transform.position.y} {Player.instance.transform.position.z}");
                //MelonLogger.Msg($"{Player.instance.transform.eulerAngles.x} {Player.instance.transform.eulerAngles.y} {Player.instance.transform.eulerAngles.z}");
            }
            else if (inDisclaimer)
            {
                TryEndDisclaimerScreen();
            }
            else if (inMainMenu)
            {
                //MelonLogger.Msg($"Zigga hmd {VRSystem.VrCamPosition.x}|{VRSystem.VrCamPosition.y}|{VRSystem.VrCamPosition.z}  {VRSystem.VrCamRotation.x}|{VRSystem.VrCamRotation.y}|{VRSystem.VrCamRotation.z}");
            }
#endif
            if (FSR_Enabled && ((inGameMain && GameMainLateStarted) || inMainMenu) && fsrScaler is not null && !fsrScaler.Initialized)
            {
                if (!secondSetup)
                {
                    secondSetup = true;
                }
                fsrScaler.Init(Camera.main);

                if (inGameMain)
                {
                    foreach (var comp in GameMenu.Singleton._globalVolume.profile.components)
                    {
                        if (comp.GetIl2CppType() == Il2CppType.Of<Exposure>())
                        {
                            var exp = comp.Cast<Exposure>();
                            exp.mode.value = ExposureMode.Automatic;
                            exp.adaptationMode.value = AdaptationMode.Progressive;
                            exp.limitMin.value = secondSetup ? 2.3f : 1.3f;
                            exp.limitMax.value = 100;
                            exp.fixedExposure.value = 1;
                            exp.compensation.value = 100;
                        }
                    }
                }
                else if (inMainMenu)
                {
                    var overrideVol = GameObject.Find("Exposure Override Volume").GetComponent<Volume>().profile.components[0];
                    overrideVol.Cast<Exposure>().fixedExposure.value = secondSetup ? 3f : 3.5f;
                }
            }
        }

        private void UpdatePlayerHandFingerRotation()
        {
            if (LeftHandOpen is null || LeftHandRootBone is null)
            {
                return;
            }
            if (RightHandOpen is null || RightHandRootBone is null)
            {
                return;
            }

            /*player hand bone structure:
            lhand
            0-lCarpal1
              0-lindex1
                0-lindex2
                 0-lindex3
              1-lmid1
                0-lmid2
                  0-lmid3
            1-lCarpal2
              0-pinky1
                0-pinky2
                 0-pinky3
              1-ring1
                0-ring2
                  0-ring3
            2-lThumb1
              0-lThumb2
                0-lThumb3
            Player.instance.leftHand.skeleton.fingerCurls*/

            //rthumb1.rotation = default rot * Player.instance.leftHand.skeleton.thumbCurl;
            //right hand finger curl is about (x 1,y 0,z -10) ratio per bone 
            //thumb is a bit different :(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> leftCurls;
            if (Player.instance.leftHand.skeleton is not null)
            {
                leftCurls = Player.instance.leftHand.skeleton.fingerCurls;
            }
            else
            {
                MelonLogger.Msg("we have no skeleton for the left");
                leftCurls = new(new float[] {
                    (SteamVRActions.hpvr_Move.active || (SteamVRActions.hpvr_GameMenu.active && SteamVRActions.hpvr_InteractUI.activeDevice == SteamVRInputSources.LeftHand) || SteamVRActions.hpvr_Pee.active || SteamVRActions.hpvr_Masturbate.active ) ? 1 : 0,
                    (SteamVRActions.hpvr_InteractUI.active && SteamVRActions.hpvr_InteractUI.activeDevice == SteamVRInputSources.LeftHand) ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0 });
            }
            LeftHandRootBone.GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[1]));
            LeftHandRootBone.GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[1]));
            LeftHandRootBone.GetChild(0).GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[1]));
            LeftHandRootBone.GetChild(0).GetChild(1).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(1).localEulerAngles + (leftHandFingerRotation * leftCurls[2]));
            LeftHandRootBone.GetChild(0).GetChild(1).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(1).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[2]));
            LeftHandRootBone.GetChild(0).GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(0).GetChild(1).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[2]));
            LeftHandRootBone.GetChild(1).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[4]));
            LeftHandRootBone.GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[4]));
            LeftHandRootBone.GetChild(1).GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[4]));
            LeftHandRootBone.GetChild(1).GetChild(1).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(1).localEulerAngles + (leftHandFingerRotation * leftCurls[3]));
            LeftHandRootBone.GetChild(1).GetChild(1).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(1).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[3]));
            LeftHandRootBone.GetChild(1).GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(1).GetChild(1).GetChild(0).GetChild(0).localEulerAngles + (leftHandFingerRotation * leftCurls[3]));
            LeftHandRootBone.GetChild(2).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(2).localEulerAngles + (leftHandThumbRotation * leftCurls[0]));
            LeftHandRootBone.GetChild(2).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(2).GetChild(0).localEulerAngles + (leftHandThumbRotation * leftCurls[0]));
            LeftHandRootBone.GetChild(2).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(LeftHandOpen.GetChild(2).GetChild(0).GetChild(0).localEulerAngles + (leftHandThumbRotation * leftCurls[0]));

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> rightCurls;
            if (Player.instance.rightHand.skeleton is not null)
            {
                rightCurls = Player.instance.rightHand.skeleton.fingerCurls;
            }
            else
            {
                MelonLogger.Msg("we have no skeleton for the rgith");
                rightCurls = new(new float[] {
                    (SteamVRActions.hpvr_Flash.active ||SteamVRActions.hpvr_SnapTurnLeft.active ||SteamVRActions.hpvr_SnapTurnRight.active ||SteamVRActions.hpvr_UI_Radial.active ||SteamVRActions.hpvr_Self_Radial.active ||
                    (SteamVRActions.hpvr_GameMenu.active && SteamVRActions.hpvr_InteractUI.activeDevice == SteamVRInputSources.RightHand) || SteamVRActions.hpvr_Combat.active || SteamVRActions.hpvr_Inventory.active ) ? 1 : 0,
                    (SteamVRActions.hpvr_InteractUI.active && SteamVRActions.hpvr_InteractUI.activeDevice == SteamVRInputSources.RightHand) ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0,
                    SteamVRActions.hpvr_Grab.active ? 1 : 0 });
            }
            RightHandRootBone.GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[1]));
            RightHandRootBone.GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[1]));
            RightHandRootBone.GetChild(0).GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[1]));
            RightHandRootBone.GetChild(0).GetChild(1).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).localEulerAngles + (rightHandFingerRotation * rightCurls[2]));
            RightHandRootBone.GetChild(0).GetChild(1).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[2]));
            RightHandRootBone.GetChild(0).GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[2]));
            RightHandRootBone.GetChild(1).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[4]));
            RightHandRootBone.GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[4]));
            RightHandRootBone.GetChild(1).GetChild(0).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(0).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[4]));
            RightHandRootBone.GetChild(1).GetChild(1).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).localEulerAngles + (rightHandFingerRotation * rightCurls[3]));
            RightHandRootBone.GetChild(1).GetChild(1).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[3]));
            RightHandRootBone.GetChild(1).GetChild(1).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(0).GetChild(1).GetChild(0).GetChild(0).localEulerAngles + (rightHandFingerRotation * rightCurls[3]));
            RightHandRootBone.GetChild(2).localRotation = Quaternion.Euler(RightHandOpen.GetChild(2).localEulerAngles + (rightHandThumbRotation * rightCurls[0]));
            RightHandRootBone.GetChild(2).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(2).GetChild(0).localEulerAngles + (rightHandThumbRotation * rightCurls[0]));
            RightHandRootBone.GetChild(2).GetChild(0).GetChild(0).localRotation = Quaternion.Euler(RightHandOpen.GetChild(2).GetChild(0).GetChild(0).localEulerAngles + (rightHandThumbRotation * rightCurls[0]));
        }

        public static void EarlyUpdate()
        {
#if !VR_DISABLED
            VRSystem.Update();
            UIManager.Update();
#endif
        }

        public override void OnLateUpdate()
        {
            if (inGameMain && GameMainLateStarted && PlayerCharacter.Player is not null && Player.instance.leftHand is not null && Player.instance.rightHand is not null && createdPlayerHandTargets)
            {
                UpdatePlayerHandFingerRotation();
            }
        }

        public void UpdateDialogueResponses()
        {
            if (Dialogue is null)
            {
                return;
            }

            foreach (var item in Dialogue.gameObject.GetComponentsInChildren<ResponseNavigationHandler>())
            {
                item.transform.localEulerAngles = new(0, 0, 0);
            }
        }

        private static void CreateHouseBoundaryFixes()
        {
            MelonLogger.Msg("Fixing sliding door floor");
            var sliderDoorFloor = new GameObject("floorFix");
            sliderDoorFloor.transform.parent = GameObject.Find("Door_slide").transform;
            sliderDoorFloor.layer = LayerMask.NameToLayer("Ground");
            sliderDoorFloor.AddComponent<BoxCollider>();
            sliderDoorFloor.transform.localPosition = new Vector3(0.4f, -0.47f, 0);

            MelonLogger.Msg("fixed floor with " + sliderDoorFloor.name);
        }

        private static void OnRadialButtonSubmit(Button button)
        {
            MelonLogger.Msg($"{RadialMenu.Singleton.IsShowing} {InteractionManager.Singleton.CurrentFocusedItem?.name}");
            if (RadialMenu.Singleton.IsShowing && InteractionManager.Singleton.CurrentFocusedItem is not null)
            {
                var option = RadialMenu.Singleton.buttons.IndexOf(button);
                if (option < 0 || option >= RadialMenu.Singleton._currentOptions.Count)
                {
                    return;
                }
                var text = RadialMenu.Singleton._currentOptions[option].Item1;
                if (RadialMenu.Singleton._currentOptions[option].Item2)
                {
                    MelonLogger.Msg("choosing " + option);
                    var inter = InteractionManager.Singleton.CurrentFocusedItem.Cast<InteractiveItem>();
                    if (inter is not null)
                    {
                        inter.OnChooseInteraction(text);
                        RadialMenu.Singleton.Toggle();
                    }
                    else
                    {
                        MelonLogger.Msg("distractablerigidbody was no interactiveItem");
                    }
                }
                else
                {
                    MelonLogger.Msg("option " + option + " is greyed out");
                }
            }
        }

        private void ScalePlayerToHMDHeight()
        {
            if (vrik is null)
            {
                return;
            }
            float sizeF = (vrik.solver.spine.headTarget.position.y - vrik.references.root.position.y) / (vrik.references.head.position.y - vrik.references.root.position.y);
            PlayerCharacter.Player.SetDefaultScaleImmediately(sizeF);
        }

        private static void TryEndDisclaimerScreen()
        {
            var disclaimer = Object.FindObjectOfType<DisclaimerManager>();
            if (!disclaimer._shouldProcessSceneTransition && !disclaimer._loadedNextScene && VRSystem.HandInputActive)
            {
                disclaimer._shouldProcessSceneTransition = true;
            }
        }

        private static void TryEndLoadingScreen()
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
            if (!loading._gameLoader.allowSceneActivation && loading._loaded && loading._transitionLoader.progress >= 0.9f && loading._gameLoader.progress >= 0.9f)
            {
                if (!shownLoadingScreenInfo)
                {
                    shownLoadingScreenInfo = true;
                    MelonLogger.Msg("loading screen done!");
                }
                if (VRSystem.HandInputActive)
                {
                    MelonLogger.Msg("started loading new scene!");
                    loading._transitionLoader.allowSceneActivation = true;
                    loading._gameLoader.allowSceneActivation = true;
                }
            }
            else if (loading._gameLoader.allowSceneActivation)
            {
                if (VRSystem.HandInputActive)
                {
                    MelonLogger.Msg("started loading new scene! 2");
                    loading._transitionLoader.allowSceneActivation = true;
                    loading._gameLoader.allowSceneActivation = true;
                }
            }
        }

        private static void TrySkipDialogue()
        {
            if (!(DialogueUI.Singleton?.IsShowing ?? false))
            {
                return;
            }

            if (DialogueUI.Singleton.dialogueText.text.Length < DialogueUI.Singleton.textOnDisplay.Length)
            {
                DialogueUI.Singleton.dialogueText.text = DialogueUI.Singleton.textOnDisplay;
                DialogueUI.Singleton.currentCharacter = DialogueUI.Singleton.textOnDisplay.Length;
                DialogueUI.Singleton.DisplayResponses();
            }
        }

        private static void UpdateHPPlayerPositiion()
        {
            if (PlayerCharacter.Player is null)
            {
                return;
            }

            PlayerCharacter.Player.transform.position = Player.instance.feetPositionGuess;
            if (PlayerCharacter.Player.Controller is not null)
            {
                PlayerCharacter.Player.Controller.enabled = false;
            }
            PlayerCharacter.Player._controlManager?.DeactivateMovement();

            //set player crouching state
            if (SteamVRCamera.instance.transform.position.y - Player.instance.transform.position.y < 0.8f)
            {
                PlayerCharacter.Player.IsCrouching = true;
            }
            else
            {
                PlayerCharacter.Player.IsCrouching = false;
            }
            //MelonLogger.Msg($"{PlayerCharacter.Player.transform.position.x} {PlayerCharacter.Player.transform.position.y} {PlayerCharacter.Player.transform.position.z}");
        }

        private static void UpdateNarratorMessage()
        {
            if (!NarratorManager.Singleton.IsShowing)
            {
                return;
            }
            else if (VRSystem.HandInputActive)
            {
                NarratorManager.Singleton.Toggle();
            }
        }

        private void CheckAndBindPlayerHandsToVRHands()
        {
#if !VR_DISABLED
            if (!createdPlayerHandTargets)
            {
                MelonLogger.Warning("Creating Player Hand IK Targets");
                CreatePlayerHandIKTargets();
                createdPlayerHandTargets = true;
                ScalePlayerToHMDHeight();
            }
#endif
        }

        private void CreatePlayerHandIKTargets()
        {
            //final ik tutorial leaked here https://github.com/suweitao8/FinalIKTutorial/blob/main/Assets/Plugins/RootMotion/FinalIK

            //according to the tutorial:
            MelonLogger.Msg("Setting up VRIK Targets");
            var vriklHand = Object.Instantiate(PlayerCharacter.Player.lHand);
            vriklHand.parent = Player.instance.leftHand.transform;
            vriklHand.localRotation = Quaternion.Euler(10, 350, 80);
            vriklHand.localPosition = new(0, 0, 0);
            var vrikrHand = Object.Instantiate(PlayerCharacter.Player.rHand);
            vrikrHand.parent = Player.instance.rightHand.transform;
            vrikrHand.localRotation = Quaternion.Euler(10, 350, 280);
            vrikrHand.localPosition = new(0, 0, 0);
            var vrikneck = Object.Instantiate(PlayerCharacter.Player.Head);
            vrikneck.parent = Player.instance.hmdTransform;
            vrikneck.localRotation = Quaternion.Euler(0, 0, 0);
            vrikneck.localPosition = new(0, -0.05f, -0.12f);
            Object.DestroyImmediate(vrikneck.GetChild(6).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(5).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(4).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(3).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(2).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(1).gameObject);
            Object.DestroyImmediate(vrikneck.GetChild(0).gameObject);

            MelonLogger.Msg("Setting up VRIK");
            PlayerCharacter.Player.FinalIK._enabled = false;
            PlayerCharacter.Player.FinalIK.BodyIK.enabled = false;

            var relaxer = PlayerCharacter.Player.lHand.parent.GetComponent<TwistRelaxer>();
            relaxer.enabled = true;
            relaxer = PlayerCharacter.Player.rHand.parent.GetComponent<TwistRelaxer>();
            relaxer.enabled = true;

            vrik = PlayerCharacter.Player.gameObject.AddComponent<VRIK>();
            vrik.enabled = false;
            vrik.solver.plantFeet = true;
            vrik.solver.locomotion.mode = IKSolverVR.Locomotion.Mode.Procedural;
            vrik.solver.locomotion.footDistance = 0.25f;
            vrik.solver.locomotion.angleThreshold = 30;
            vrik.solver.spine.headTarget = vrikneck;
            vrik.solver.leftArm.target = vriklHand;
            vrik.solver.rightArm.target = vrikrHand;
            vrik.AutoDetectReferences();
            //vrik.GuessHandOrientations();
            vrik.enabled = true;
            MelonLogger.Msg("Done setting VRIK up");

            //Create interactionobject for finalik
            MelonLogger.Msg("creating left hand open copy");
            LeftHandOpen = new GameObject("lHand").transform;
            LeftHandOpen.parent = Player.instance.leftHand.transform;
            LeftHandOpen.localRotation = Quaternion.Euler(290, 100, 330);
            LeftHandRootBone = PlayerCharacter.Player.lHand;
            var lCarpal1 = new GameObject("lCarpal1").transform;
            lCarpal1.parent = LeftHandOpen;
            lCarpal1.localRotation = Quaternion.Euler(0, 0, 0);
            lCarpal1.localPosition = new(-0.0159f, 0.0061f, 0.0227f);
            var lIndex1 = new GameObject("lIndex1").transform;
            lIndex1.parent = lCarpal1;
            lIndex1.localRotation = Quaternion.Euler(3.6095f, 1.1948f, 0.8308f);
            lIndex1.localPosition = new(-0.0408f, 0.0042f, 0.0384f);
            var lIndex2 = new GameObject("lIndex2").transform;
            lIndex2.parent = lIndex1;
            lIndex2.localRotation = Quaternion.Euler(8.4157f, 3.5968f, 7.6962f);
            lIndex2.localPosition = new(-0.0326f, -0.0005f, 0.0216f);
            var lIndex3 = new GameObject("lIndex3").transform;
            lIndex3.parent = lIndex2;
            lIndex3.localRotation = Quaternion.Euler(9.1338f, 3.9899f, 9.0933f);
            lIndex3.localPosition = new(-0.018f, -0.001f, 0.0105f);
            var lMid1 = new GameObject("lMid1").transform;
            lMid1.parent = lCarpal1;
            lMid1.localRotation = Quaternion.Euler(9.2008f, 3.3929f, 6.8076f);
            lMid1.localPosition = new(-0.0531f, 0.002f, 0.021f);
            var lMid2 = new GameObject("lMid2").transform;
            lMid2.parent = lMid1;
            lMid2.transform.localRotation = Quaternion.Euler(10.9773f, 4.8565f, 11.7279f);
            lMid2.transform.localPosition = new(-0.0382f, 0.0006f, 0.0201f);
            var lMid3 = new GameObject("lMid3").transform;
            lMid3.parent = lMid2;
            lMid3.localRotation = Quaternion.Euler(12.93f, 6.6297f, 16.2434f);
            lMid3.localPosition = new(-0.0218f, -0.0024f, 0.0124f);
            var lCarpal2 = new GameObject("lCarpal2").transform;
            lCarpal2.parent = LeftHandOpen;
            lCarpal2.localRotation = Quaternion.Euler(0, 0, 0);
            lCarpal2.localPosition = new(-0.0286f, -0.0003f, -0.0001f);
            var lPinky1 = new GameObject("lPinky1").transform;
            lPinky1.parent = lCarpal2;
            lPinky1.localRotation = Quaternion.Euler(9.8077f, 7.0431f, 11.084f);
            lPinky1.localPosition = new(-0.047f, -0.0065f, 0.0048f);
            var lPinky2 = new GameObject("lPinky2").transform;
            lPinky2.parent = lPinky1;
            lPinky2.localRotation = Quaternion.Euler(10.6143f, 5.7524f, 12.9547f);
            lPinky2.localPosition = new(-0.0263f, -0.0014f, 0.0087f);
            var lPinky3 = new GameObject("lPinky3").transform;
            lPinky3.parent = lPinky2;
            lPinky3.localRotation = Quaternion.Euler(11.6115f, 6.8335f, 16.9866f);
            lPinky3.localPosition = new(-0.0159f, -0.0008f, 0.0053f);
            var lRing1 = new GameObject("lRing1").transform;
            lRing1.parent = lCarpal2;
            lRing1.localRotation = Quaternion.Euler(10.9208f, 5.6854f, 11.8102f);
            lRing1.localPosition = new(-0.0439f, 0.0034f, 0.0245f);
            var lRing2 = new GameObject("lRing2").transform;
            lRing2.parent = lRing1;
            lRing2.localRotation = Quaternion.Euler(10.8787f, 5.5033f, 12.0991f);
            lRing2.localPosition = new(-0.037f, -0.0019f, 0.0149f);
            var lRing3 = new GameObject("lRing3").transform;
            lRing3.parent = lRing2;
            lRing3.localRotation = Quaternion.Euler(12.2844f, 6.7195f, 16.3946f);
            lRing3.localPosition = new(-0.0202f, -0.0013f, 0.0085f);
            var lThumb1 = new GameObject("lThumb1").transform;
            lThumb1.parent = LeftHandOpen;
            lThumb1.localRotation = Quaternion.Euler(12.8347f, 348.3466f, 5.5996f);
            lThumb1.localPosition = new(0.0018f, -0.0013f, 0.0271f);
            var lThumb2 = new GameObject("lThumb2").transform;
            lThumb2.parent = lThumb1;
            lThumb2.localRotation = Quaternion.Euler(350.1626f, 0.616f, 0.7598f);
            lThumb2.localPosition = new(-0.0066f, -0f, 0.0405f);
            var lThumb3 = new GameObject("lThumb3").transform;
            lThumb3.parent = lThumb2;
            lThumb3.localRotation = Quaternion.Euler(358.9155f, 351.3624f, 2.0598f);
            lThumb3.localPosition = new(-0.0138f, -0.0061f, 0.0258f);

            MelonLogger.Msg("creating right hand open copy");
            RightHandOpen = new GameObject("rHand").transform;
            RightHandOpen.parent = Player.instance.rightHand.transform;
            RightHandOpen.localRotation = Quaternion.Euler(290, 100, 330);
            RightHandRootBone = PlayerCharacter.Player.rHand;
            var rCarpal1 = new GameObject("rCarpal1").transform;
            rCarpal1.parent = RightHandOpen;
            rCarpal1.localRotation = Quaternion.Euler(0, 0, 0);
            rCarpal1.localPosition = new(0.0158f, 0.0056f, 0.0228f);
            var rIndex1 = new GameObject("rIndex1").transform;
            rIndex1.parent = rCarpal1;
            rIndex1.localRotation = Quaternion.Euler(5.7488f, 357.6187f, 356.1321f);
            rIndex1.localPosition = new(0.041f, 0.0048f, 0.0379f);
            var rIndex2 = new GameObject("rIndex2").transform;
            rIndex2.parent = rIndex1;
            rIndex2.localRotation = Quaternion.Euler(10.1488f, 355.3312f, 348.8887f);
            rIndex2.localPosition = new(0.0326f, -0.0005f, 0.0217f);
            var rIndex3 = new GameObject("rIndex3").transform;
            rIndex3.parent = rIndex2;
            rIndex3.localRotation = Quaternion.Euler(10.8349f, 354.8885f, 347.4843f);
            rIndex3.localPosition = new(0.018f, -0.001f, 0.0105f);
            var rMid1 = new GameObject("rMid1").transform;
            rMid1.parent = rCarpal1;
            rMid1.localRotation = Quaternion.Euler(7.5077f, 357.7875f, 356.568f);
            rMid1.localPosition = new(0.0533f, 0.0024f, 0.0205f);
            var rMid2 = new GameObject("rMid2").transform;
            rMid2.parent = rMid1;
            rMid2.localRotation = Quaternion.Euler(9.3358f, 356.0557f, 351.7078f);
            rMid2.localPosition = new(0.0381f, 0.0006f, 0.0202f);
            var rMid3 = new GameObject("rMid3").transform;
            rMid3.parent = rMid2;
            rMid3.transform.localRotation = Quaternion.Euler(11.3442f, 354.4087f, 347.3017f);
            rMid3.transform.localPosition = new(0.0218f, -0.0024f, 0.0125f);
            var rCarpal2 = new GameObject("rCarpal2").transform;
            rCarpal2.parent = RightHandOpen;
            rCarpal2.localRotation = Quaternion.Euler(0, 0, 0);
            rCarpal2.localPosition = new(0.0286f, 0.0003f, -0.0002f);
            var rPinky1 = new GameObject("rPinky1").transform;
            rPinky1.parent = rCarpal2;
            rPinky1.localRotation = Quaternion.Euler(9.173f, 354.1744f, 352.6472f);
            rPinky1.localPosition = new(0.0465f, -0.0071f, 0.006f);
            var rPinky2 = new GameObject("rPinky2").transform;
            rPinky2.parent = rPinky1;
            rPinky2.localRotation = Quaternion.Euler(9.7769f, 355.0669f, 350.8191f);
            rPinky2.localPosition = new(0.0263f, -0.0014f, 0.0088f);
            var rPinky3 = new GameObject("rPinky3").transform;
            rPinky3.parent = rPinky2;
            rPinky3.localRotation = Quaternion.Euler(10.8329f, 354.0421f, 346.839f);
            rPinky3.localPosition = new(0.0159f, -0.0008f, 0.0053f);
            var rRing1 = new GameObject("rRing1").transform;
            rRing1.parent = rCarpal2;
            rRing1.localRotation = Quaternion.Euler(10.0206f, 355.6122f, 351.8123f);
            rRing1.localPosition = new(0.0439f, 0.0028f, 0.0246f);
            var rRing2 = new GameObject("rRing2").transform;
            rRing2.parent = rRing1;
            rRing2.localRotation = Quaternion.Euler(9.7437f, 355.339f, 351.6165f);
            rRing2.localPosition = new(0.037f, -0.0019f, 0.0149f);
            var rRing3 = new GameObject("rRing3").transform;
            rRing3.parent = rRing2;
            rRing3.localRotation = Quaternion.Euler(11.2204f, 354.2007f, 347.3634f);
            rRing3.localPosition = new(0.0201f, -0.0014f, 0.0085f);
            var rThumb1 = new GameObject("rThumb1").transform;
            rThumb1.parent = RightHandOpen;
            rThumb1.localRotation = Quaternion.Euler(16.2263f, 4.4125f, 353.1956f);
            rThumb1.localPosition = new(-0.0017f, -0.0012f, 0.0271f);
            var rThumb2 = new GameObject("rThumb2").transform;
            rThumb2.parent = rThumb1;
            rThumb2.localRotation = Quaternion.Euler(350.1758f, 359.5684f, 359.2524f);
            rThumb2.localPosition = new(0.0066f, 0.0002f, 0.0405f);
            var rThumb3 = new GameObject("rThumb3").transform;
            rThumb3.parent = rThumb2;
            rThumb3.localRotation = Quaternion.Euler(358.9277f, 8.5045f, 357.893f);
            rThumb3.localPosition = new(0.0138f, -0.006f, 0.0259f);

            LeftHandOpen.hideFlags = HideFlags.HideAndDontSave;
            RightHandOpen.hideFlags = HideFlags.HideAndDontSave;
            MelonLogger.Msg("Done with all VRIK setup");
        }

        private void CreateMainMenuBoundary()
        {
            colliders.Clear();
            GameObject floor = GameObject.Find("Floor");
            Material m = new(floor.GetComponent<MeshRenderer>().material);

            floor.layer = LayerMask.NameToLayer("Ground");
            floor.AddComponent<BoxCollider>().includeLayers = LayerMask.GetMask("Walls", "Ground", "Ragdolls", "InteractiveItems");

            //set up colliders around the menu area so we cannot fall off
            var border1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border1.layer = LayerMask.NameToLayer("Walls");
            border1.GetComponent<MeshRenderer>().material = m;
            border1.transform.position = new Vector3(5, 5.5f, -8);
            border1.transform.localScale = new Vector3(1, 12, 20);
            border1.name = "HPVR collider right";
            var border2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border2.layer = LayerMask.NameToLayer("Walls");
            border2.GetComponent<MeshRenderer>().material = m;
            border2.transform.position = new Vector3(-6, 5.5f, -8);
            border2.transform.localScale = new Vector3(1, 12, 20);
            border2.name = "HPVR collider left";
            var border3 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border3.layer = LayerMask.NameToLayer("Walls");
            border3.GetComponent<MeshRenderer>().material = m;
            border3.transform.position = new Vector3(0, 5.5f, -14.5f);
            border3.transform.localScale = new Vector3(12, 12, 1);
            border3.name = "HPVR collider back";
            var border4 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border4.layer = LayerMask.NameToLayer("Walls");
            border4.GetComponent<MeshRenderer>().material = m;
            border4.transform.position = new Vector3(0, 5.5f, -1);
            border4.transform.localScale = new Vector3(12, 12, 1);
            border4.name = "HPVR collider front";
            var border5 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border5.layer = LayerMask.NameToLayer("Walls");
            border5.GetComponent<MeshRenderer>().material = m;
            border5.transform.position = new Vector3(-0.5f, 11, -8);
            border5.transform.localScale = new Vector3(14, 1, 20);
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
            foreach (var col in colliders)
            {
                col.includeLayers = LayerMask.GetMask("Walls", "Ground", "Ragdolls", "InteractiveItems");
            }
        }

        private void GameMainLateStart()
        {
            MelonLogger.Msg("late start");
            GameMainLateStarted = true;

            if (!HDDynamicResolutionPlatformCapabilities.DLSSDetected && FSR_Enabled)
            {
                SetUpFSR3();
            }

#if !VR_DISABLED
            CreateHouseBoundaryFixes();
            UpdateInteractiveItems();

            var mask = LayerMask.GetMask("Default", "UI", "InteractiveItems", "InteractiveItemsHighlighted", "Ragdolls", "Ground", "Walls");
            if (Laser.LeftLaser is not null)
            {
                Laser.LeftLaser.LaserMask = mask;
            }

            if (Laser.RightLaser is not null)
            {
                Laser.RightLaser.LaserMask = mask;
            }

            PlayerCharacter.Player.Controller.enabled = false;
            PlayerCharacter.Player.Mecanim.Animator.stabilizeFeet = true;
            //PlayerCharacter.Player._controlManager.DeactivateMovement();
            //PlayerCharacter.Player.PuppetMaster.Puppet.gameObject.SetActive(false);

            MelonLogger.Msg("disabling Volumetric Fog");
            GameMenu.Singleton._fallbackFogGlobalVolume.SetActive(true);

            VRSystem.SyncPlayerAndHMD();

            if (VRSystem.SetUpInput)
            {
                SteamVRActions.hpvr_InteractUI.onStateUp += (state, source) => TrySkipDialogue();
                SteamVRActions.hpvr_InteractUI.onStateUp += (state, source) => UpdateDialogueResponses();
            }

            Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
            Player.instance.transform.position = new(0.65f, 0, 3.55f);

            MelonLogger.Msg("removing player colliders");
            foreach (var coll in PlayerCharacter.Player.GetColliders)
            {
                coll.enabled = false;
            }
            MelonLogger.Msg("removing player colliders2");
            foreach (var coll in PlayerCharacter.Player.PuppetMaster.transform.FindDeepChild(PlayerCharacter.Player.Gender == Genders.Female ? "lShldrBend" : "lShldr").GetComponentsInChildren<Collider>())
            {
                coll.enabled = false;
            }
            MelonLogger.Msg("removing player colliders3");
            foreach (var coll in PlayerCharacter.Player.PuppetMaster.transform.FindDeepChild(PlayerCharacter.Player.Gender == Genders.Female ? "rShldrBend" : "rShldr").GetComponentsInChildren<Collider>())
            {
                coll.enabled = false;
            }

            MelonLogger.Msg("removing cinemachine");
            var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
            cinemachineBrain.enabled = false;

            MelonLogger.Msg("adding height cal button");
            //add button to settings menu for height calibration
            var restart = GameObject.Instantiate(GameMenu.Singleton.Canvas.transform.GetChild(0).GetChild(0).GetChild(2).GetChild(1).gameObject);
            restart.transform.parent = GameMenu.Singleton.Canvas.transform.GetChild(0).GetChild(0).GetChild(2).transform;
            restart.transform.localScale = Vector3.one;
            restart.transform.localPosition = new(0, -310, 0);
            restart.GetComponentInChildren<Text>().text = "Recalibrate Height";
            Button button = restart.GetComponent<Button>();
            button ??= restart.GetComponentInChildren<Button>();
            button.onClick.m_PersistentCalls = new();
            button.onClick.m_Calls = new();
            button.onClick.AddListener(new Action(() =>
            {
                MelonLogger.Msg("rescaling player height");
                HPVR.Instance?.ScalePlayerToHMDHeight();
            }));
            var background = GameMenu.Singleton.Canvas.GetComponent<RectTransform>();
            background.sizeDelta = new(700, 820);
            background.pivot = new(0.5f, 0.7f);

            //this one might crash so we do it last
            MelonLogger.Msg("Setting um canvases");
            SetUpInGameCanvas();
#endif
        }

        private void UpdateCameraCulling()
        {
#if !VR_DISABLED
            SteamVRCamera.instance.camera.cullingMask &= ~LayerMask.GetMask("InvisibleToMainCamera");

            updatedCameraCull = true;
#endif
        }

        private void SetUpDialogueCanvas()
        {
            if (Dialogue is null)
            {
                return;
            }

            if (!Dialogue.gameObject.active)
            {
                return;
            }

            MelonLogger.Msg("updating positions of dialogue UI");

            Dialogue.transform.FindDeepChild("MoveCameraReminder").gameObject.SetActive(false);
            Dialogue.transform.FindDeepChild("AvatarComponents").localPosition = new(-400, 400, 0);
            Dialogue.transform.FindDeepChild("Stats").localPosition = new(100, 400, 0);
            Dialogue.transform.FindDeepChild("NameContainer").localPosition = new(-50, 120, 0);
            Dialogue.transform.FindDeepChild("WhiteBorder").localPosition = new(0, 0, 0);

            var text = Dialogue.transform.FindDeepChild("DialogueText");
            text.localPosition = new(0, 200, 0);
            text.GetComponent<RectTransform>().sizeDelta = new(1000, 200);

            var responses = Dialogue.transform.FindDeepChild("Responses");
            responses.localPosition = new(0, 0, 0);
            responses.localEulerAngles = new(0, 0, 0);
            responses.localScale = new(1.2f, 1.2f, 0);
            responses.GetComponent<RectTransform>().sizeDelta = new(2000, 2000);

            var scrollView = Dialogue.transform.FindDeepChild("Scroll View");
            scrollView.localEulerAngles = new(0, 0, 0);
            scrollView.localScale = new(1, 1, 1);
            scrollView.localPosition = new(0, -700, 0);

            Dialogue.transform.GetChild(0).localPosition = new Vector3(0, -300, 0);
        }

        private void SetUpInGameCanvas()
        {
            foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
            {
                Canvas canvas = obj.Cast<Canvas>();
                switch (canvas.gameObject.name)
                {
                    case "Relationship Notificatiops Canvas":
                        canvas.MoveContents(new Vector3(200, 0, 0));
                        goto case "GameOverCanvas";
                    case "BGCUICanvas":
                        canvas.MoveContents(new Vector3(0, -400, 0));
                        canvas.GetComponent<RectTransform>().sizeDelta *= 0.7f;
                        goto case "GameOverCanvas";
                    case "NarrartorCanvas":
                        canvas.MoveContents(new Vector3(0, 300, 0));
                        canvas.GetComponent<RectTransform>().sizeDelta *= 0.7f;
                        goto case "GameOverCanvas";
                    case "OrgasmManager":
                        canvas.MoveContents(new Vector3(0, -200, 0));
                        goto case "GameOverCanvas";
                    case "MiniGameCanvas":
                    case "CombatManager":
                    case "ScreenFadeCanvas":
                    case "GameOverCanvas":
                        canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        break;
                    default:
                        if ((canvas.transform?.parent?.name is (
                            "OpportunityWindow"
                            or "QuestPopup"
                            or "Messages"
                            or "InputManager2"
                            or "ThrowMeter" //todo fix at all
                            )) || (canvas.transform?.name is (
                            "OpportunityWindow"
                            or "QuestPopup"
                            or "Messages"
                            or "InputManager2"
                            or "ThrowMeter"
                            )))
                        {
                            canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        }
                        break;

                    //dont add to these
                    case "UIRadialMenuCanvas": //uiradial = messages, opportunity window open radial
                        canvas.transform.localScale *= 1.7f;
                        goto case "UseSelectCanvas";
                    case "DebugCanvas": //debug log
                        canvas.transform.localScale *= 1.3f;
                        goto case "UseSelectCanvas";
                    case "GameMenuCanvas":
                    case "AudioSettingsCanvas":
                    case "GameplaySettingsCanvas":
                    case "GraphicsMenuCanvas":
                    case "ConsoleCanvas":
                    case "SaveCanvas":
                    case "LoadCanvas":
                    case "CameraView":
                    case "MadisonPhoneCanvas":
                    case "UseSelectCanvas":
                        break;
                    case "DialogueCanvas":
                        Dialogue = canvas;
                        Dialogue.transform.localScale *= 1.1f;
                        break;
                    case "RadialMenuCanvas":
                        UIManager.CanvasToIgnore.Add(canvas.name);
                        //hook all canvas buttons
                        foreach (var button in canvas.GetComponentsInChildren<Button>())
                        {
                            button.GetComponent<UIElement>().OnSubmit += () =>
                            {
                                OnRadialButtonSubmit(button);
                            };
                        }
                        canvas.transform.localScale *= 1.5f;
                        break;
                    case "InventoryCanvas":
                        break;
                    case "InteractionCanvas":
                        canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        UIManager.CanvasToIgnore.Add(canvas.name);
                        interactionCanvas = canvas;
                        break;
                }
            }
        }

        private void TryDisableMoveDuringDialogue()
        {
            if (!DialogueVisible && DialogueUI.Singleton.IsShowing)
            {
                if (DialogueSpeaker is null)
                {
                    DialogueSpeaker = DialogueUI.Singleton.CurrentSpeaker;
                    DialogueVisible = true;
                    MelonLogger.Msg("set item and dialogue visible");
                }
                SetUpDialogueCanvas();
            }
            //disable movement only when far enough away
            else if (DialogueVisible && DialogueUI.Singleton.IsShowing)
            {
                if (DialogueSpeaker is not null && VRSystem.MovementEnabled)
                {
                    if (DistanceEvaluator.EvaluateOne(DialogueSpeaker.gameObject, SteamVRCamera.instance.gameObject, 2.3f, GreaterThanLessThanEquations.GreaterThan))
                    {
                        MelonLogger.Msg("more than 2.3f away");
                        VRSystem.MovementEnabled = false;
                    }
                }
            }
            else if (DialogueVisible && !DialogueUI.Singleton.IsShowing)
            {
                DialogueSpeaker = null;
                DialogueVisible = false;
                VRSystem.MovementEnabled = true;
                MelonLogger.Msg("unset item and dialogue visibility");
            }
            else if (!DialogueVisible && !DialogueUI.Singleton.IsShowing && DialogueSpeaker is not null)
            {
                DialogueSpeaker = null;
            }
        }

        private void UpdateInteractionCanvas()
        {
            if (interactionCanvas is null)
            {
                return;
            }
            if (interactionCanvas.enabled)
            {
                interactionCanvas.enabled = false;
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
                    //MelonLogger.Msg("iteminteractable checking: " + item.name + ":" + item.SpecialItemType.ToString());
                    Items.Add(item);
                    var inter = item.gameObject.AddComponent<Interactable>();
                    item.gameObject.AddComponent<RadialInteractable>();
                    inter.highlightOnHover = false;
                    inter.handFollowTransform = true;
                    inter.snapAttachEaseInTime = 0.15f;
                    inter.useHandObjectAttachmentPoint = true;
                    inter.hideHandOnAttach = false;

                    if (item.gameObject.layer == II || item.gameObject.layer == IIHighlighted)
                    {
                        //this seems to include most items for now
                        if (item.canBeGrabbed)
                        {
                            item.gameObject.AddComponent<GrabbableInteractable>();
                            //var velocity = item.gameObject.AddComponent<VelocityEstimator>();
                            //velocity.velocityAverageFrames = 5;
                            //velocity.angularVelocityAverageFrames = 11;
                            //velocity.estimateOnAwake = false;

                            //var throwable = item.gameObject.AddComponent<Throwable>();
                            //throwable.attachmentFlags = Hand.AttachmentFlags.SnapOnAttach | Hand.AttachmentFlags.DetachFromOtherHand | Hand.AttachmentFlags.ParentToHand | Hand.AttachmentFlags.TurnOnKinematic;
                            //throwable.catchingSpeedThreshold = -1;
                            //throwable.releaseVelocityStyle = ReleaseStyle.ShortEstimation;
                            //throwable.releaseVelocityTimeOffset = -0.011f;
                            //throwable.scaleReleaseVelocity = 1.1f;
                            //throwable.scaleReleaseVelocityThreshold = -1;
                            //throwable.scaleReleaseVelocityCurve = AnimationCurve.EaseInOut(0.0f, 0.1f, 1.0f, 1.0f);
                            //throwable.restoreOriginalParent = true;

                            var rigid = item.GetComponent<Rigidbody>();
                            rigid ??= item.GetComponentInChildren<Rigidbody>();
                            rigid ??= item.GetComponentInParent<Rigidbody>();
                            if (rigid is not null)
                            {
                                rigid.useGravity = true;
                            }
                        }
                    }
                }
            }
        }
    }
}