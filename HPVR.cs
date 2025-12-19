using HPVR.Gameplay;
using HPVR.UI;
using HPVR.utils;
using HPVR.VR;
using Il2Cpp;
using Il2CppCinemachine;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents;
using Il2CppEekEvents.Helper;
using Il2CppHouseParty;
using Il2CppInterop.Runtime;
using MelonLoader;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR
{
    public class HPVR : MelonMod
    {
        public static HPVR? Instance { get; private set; }
        public bool inGameMain = false;
        public bool inMainMenu = false;
        public bool inLoadingScreen;
        public bool inDisclaimer;
        private bool removedPlayerHead = false;
        private readonly List<BoxCollider> colliders = new(5);
        public Transform? playerChar;
        private bool boundPlayerHands;
        private Canvas? interactionCanvas;
        private Canvas? RadialCanvas;
        private Canvas? ScreenFade;

        public static bool Enabled { get; internal set; } = true;

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
            Instance ??= this;
        }

        ~HPVR()
        {
            Enabled = false;
        }

        //##################################################################
        //##################################################################
        //##
        //##    Steps still left to do before release:
        //##    - Main menu UI has to be fully workable
        //##    - in game items have to work on ui click, not necessarily with physics
        //##    - add custom loading screen
        //##    - dialogue in game needs to work
        //##    - bind controllers to all actions needed to play through the game, so 
        //##        - Inventory, memories and Opportunity with the Q radial
        //##        - Game Menu
        //##        - E Radial

        //todos:
        //remove cinemachinebrain during cutscenes and loading screen (like with third person camera)
        //ui interaction?
        //bind controllers
        //put interactable script on everything with interactive item
        //player hands have a monobehaviour handposer on them. might need to remove for vr
        //player hand ik bind to gloves
        //maybe do IK with the player object -> finalik dokumentation
        //curve ui canvases slightly
        //build a keyboard? using maybe Ikeyboardevent

        public override void OnInitializeMelon()
        {
            try
            {
                VRSystem.StartVR();
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
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            inGameMain = sceneName == "GameMain";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";

            VRSystem.Gravity = inMainMenu || inGameMain;

            VRSystem.SetUpSteamVRUnity();

            UIManager.Initialize();

            MelonLogger.Msg("[HPVR] preparing scene " + sceneName);
            removedPlayerHead = false;

            UIManager.UpdateUIPos = true;

            if (inGameMain)
            {
                playerChar = PlayerCharacter.Player.transform;

                Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                Player.instance.transform.position = new(0.65f, 0, 3.55f);

                if (inGameMain && PlayerCharacter.Player is not null)
                {
                    RemovePlayerHead();
                }

                Player.instance.leftHand.useHoverSphere = true;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = true;
                Player.instance.rightHand.useHoverSphere = true;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = true;

                PlayerCharacter.add_OnPlayerLateStart(new Action(() => GameMainLateStart()));
            }
            else if (inMainMenu)
            {
                Player.instance.leftHand.useHoverSphere = false;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = true;
                Player.instance.rightHand.useHoverSphere = false;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = true;

                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);
                Player.instance.transform.position = new(0.55f, 0, -10);

                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;

                CreateMainMenuBoundary();

                UIManager.UpdateUIPos = false;

                QualitySettings.SetQualityLevel(1);
            }
            else if (Player.instance is not null)
            {
                Player.instance.leftHand.useHoverSphere = false;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = false;
                Player.instance.rightHand.useHoverSphere = false;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = true;
                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);
            }

            if (inLoadingScreen)
            {
                var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
                cinemachineBrain.gameObject.SetActive(false);
            }

            UIManager.OnSceneChange();
            Hand.UpdateScene();

            if (!inGameMain)
            {
                Player.instance.leftHand.GetComponent<Laser>().LaserMask = Laser.DefaultLaserMask;
                Player.instance.rightHand.GetComponent<Laser>().LaserMask = Laser.DefaultLaserMask;
            }

            MelonLogger.Msg("[HPVR] scene preparation done for " + sceneName);
        }

        private void GameMainLateStart()
        {
            MelonLogger.Msg("late start");

            CreateHouseBoundaryFixes();
            UpdateInteractiveItems();

            Player.instance.leftHand.GetComponent<Laser>().LaserMask = InteractionManager.Singleton._primaryIMgrMask | LayerMask.NameToLayer("UI");
            Player.instance.rightHand.GetComponent<Laser>().LaserMask = InteractionManager.Singleton._primaryIMgrMask | LayerMask.NameToLayer("UI");

            //turn off player model and collision for now
            PlayerCharacter.Player._bodySkinnedMeshRenderer.enabled = false;
            GameObject.Find("CH_PlayerFemale")?.SetActive(false);
            GameObject.Find("CH_PlayerMale")?.SetActive(false);
            foreach (var coll in PlayerCharacter.Player.GetColliders)
            {
                coll.enabled = false;
            }

            MelonLogger.Msg("disabling Volumetric Fog");
            var fogs = GameObject.FindObjectsOfType<Volume>(true);
            foreach (var f in fogs)
            {
                if (f?.name == "Fallback Fog Global Volume")
                {
                    f.gameObject.SetActive(true);
                    f.priority = 9999;
                }
            }

            VRSystem.SyncPlayerAndHMD();

            //this one might crash so we do it last
            SetUpInGameCanvas();
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

        private void SetUpInGameCanvas()
        {
            //todo add the screenfade canvas here, and keep it very close in front of the camera so it covers all view
            // special handling for some canvas
            var interaction = GameObject.Find("InteractionCanvas"); //crosshair and text
            var radial = GameObject.Find("RadialMenuCanvas"); //interaction radial, you, item, character 
            var fade = GameObject.Find("ScreenFadeCanvas"); //interaction radial, you, item, character 
            UIManager.CanvasToIgnore.Add(interaction?.transform);
            UIManager.CanvasToIgnore.Add(radial?.transform);
            UIManager.CanvasToIgnore.Add(fade?.transform);
            interactionCanvas = interaction?.GetComponent<Canvas>();
            RadialCanvas = radial?.GetComponent<Canvas>();
            ScreenFade = fade?.GetComponent<Canvas>();
            //todo only do for some types ui, namely the ones that always show and interaction target
            //dialogue ui
            //stamina
            //bgc 
            //message bubbles
            //big disclaimer text
            //reticle
            //

            foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
            {
                Canvas canvas = obj.Cast<Canvas>();
                switch (canvas.gameObject.name)
                {
                    case "DialogueCanvas":
                    case "InteractionCanvas":
                    case "InventoryCanvas":
                    case "BGCUICanvas":
                    case "UseSelectCanvas":
                    case "OrgasmManager":
                    case "NarrartorCanvas":
                    case "DebugCanvas": //debug log
                    case "SaveCanvas":
                    case "LoadCanvas":
                    case "GameOverCanvas":
                    case "GameMenuCanvas":
                    case "GraphicsMenuCanvas":
                    case "ConsoleCanvas":
                    case "MiniGameCanvas":
                    case "CombatManager":
                    case "Relationship Notificatiops Canvas":
                    case "AudioSettingsCanvas":
                    case "GameplaySettingsCanvas":
                    case "UIRadialMenuCanvas": //uiradial = messages, opportunity window open radial
                        canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        break;
                    default:
                        if (canvas.transform?.parent?.name is (
                            "OpportunityWindow"
                            or "QuestPopup"
                            or "Messages"
                            or "InputManager2"
                            or "ThrowMeter"))
                        {
                            canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        }
                        break;
                }

            }
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

        public override void OnUpdate()
        {
            if (SteamVR_Camera.instance?.transform is null)
            {
                return;
            }

            if (inGameMain)
            {
                UpdateHPPlayerPositiion();
                if (!removedPlayerHead)
                {
                    RemovePlayerHead();
                }
                if (!boundPlayerHands)
                {
                    BindPlayerHandsToVRHands();
                }

                UpdateInteractiveItems();

                if (interactionCanvas is not null)
                {
                    UpdateInteractionCanvas();
                }

                //dont need it now
                //ScalePlayerToHMDHeight();
            }
            else if (inLoadingScreen)
            {
                TryEndLoadingScreen();
            }
            else if (inDisclaimer)
            {
                TryEndDisclaimerScreen();
            }

            VRSystem.Update();
            UIManager.Update();
        }

        private void UpdateInteractionCanvas()
        {
            if (interactionCanvas is null)
            {
                return;
            }

            Transform camera = SteamVR_Camera.instance.transform;
            if (Laser.LastHit.point != Vector3.zero)
            {
                interactionCanvas.transform.position = Laser.LastHit.point + (camera.rotation * Vector3.forward * -0.05f);
            }
            else
            {
                interactionCanvas.transform.position = camera.position + (camera.rotation * Vector3.forward * 1.45f);
            }
            //maybe this works, we'll see. or its 180 flipped
            interactionCanvas.transform.LookAt(camera.position);
        }

        private void UpdateHPPlayerPositiion()
        {
            var diff = Player.instance.transform.position - PlayerCharacter.Player.transform.position;
            PlayerCharacter.Player.Controller.Move(diff);
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
            if (!loading._gameLoader.allowSceneActivation && loading._loaded && loading._gameLoader.progress >= 0.9f && VRSystem.HandInputActive)
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
                    MelonLogger.Msg("iteminteractable checking: " + item.name + ":" + item.SpecialItemType.ToString());
                    Items.Add(item);
                    var inter = item.gameObject.AddComponent<Interactable>();
                    item.gameObject.AddComponent<RadialInteractable>();

                    //add special handlers apart from radial
                    if (item.SpecialItemType == Il2CppEekEvents.Items.SpecialItemTypes.None)
                    {
                        item.gameObject.AddComponent<VelocityEstimator>();
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
                        MelonLogger.Msg("Added ItemInteractible onto " + item.gameObject.name);
                        //todo add handposer depending on the type of collider we find/what object it really is
                        //todo not only mount an interactive item to the hand but keep it relative to where the hand was when grabbing
                    }
                    else
                    {
                        inter.highlightOnHover = false;
                        inter.useHandObjectAttachmentPoint = false;
                    }
                }
            }
        }

        private void BindPlayerHandsToVRHands()
        {
            if (PlayerCharacter.Player is null || Player.instance.leftHand is null || Player.instance.rightHand is null)
            {
                return;
            }

            PlayerCharacter.Player.FinalIK.BodyIK.solver.leftHandEffector.target = Player.instance.leftHand.transform;
            PlayerCharacter.Player.FinalIK.BodyIK.solver.rightHandEffector.target = Player.instance.rightHand.transform;

            boundPlayerHands = true;
        }

        private void ScalePlayerToHMDHeight()
        {
            //todo also move vrplayer to player height when moving the playercharacter in game

            //if (inGameMain)
            //{
            //    if (!PlayerCharacter.Player.IsImmobile && playerChar is not null)
            //    {
            //        vrPlayer.transform.position += new Vector3(lastControllerMove.x, 0, lastControllerMove.z);
            //        vrPlayer.transform.position = new(vrPlayer.transform.position.x, playerChar.position.y, vrPlayer.transform.position.z);
            //    }
            //}
            //MelonLogger.Msg(hmdAbsoluteLastPosition.y);
            var headSetHeight = SteamVR_Camera.instance.transform.position.y;
            if (headSetHeight > 1f)
            {
                if (PlayerCharacter.Player.Gender == Genders.Male)
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(headSetHeight / 1.75f);
                }
                else
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(headSetHeight / 1.65f);
                }
                PlayerCharacter.Player.IsCrouching = false;
            }
            else
            {
                if (PlayerCharacter.Player.Gender == Genders.Male)
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(headSetHeight / 0.8f);
                }
                else
                {
                    PlayerCharacter.Player.SetDefaultScaleImmediately(headSetHeight / 0.73f);
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
    }
}