using HPVR.Gameplay;
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
using MelonLoader;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
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
        private Canvas? Dialogue;
        private InteractiveItem? itemWhenDialogueStart = null;
        private static bool shownLoadingScreenInfo = false;
        private bool DialogueVisible = false;
        int IIHighlighted = LayerMask.NameToLayer("InteractiveItemsHighlighted");
        int II = LayerMask.NameToLayer("InteractiveItems");

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
        //##    - bind controllers to all actions needed to play through the game, so 
        //##        - Inventory, memories and Opportunity with the Q radial
        //##        - Game Menu
        //##        - E Radial

        //todos:
        //bind controllers
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

        //todo fix crash here on all scenes but gamemain
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
            shownLoadingScreenInfo = false;
            //int counter = 0;
            //MelonLogger.Msg((counter++).ToString());
            if (inGameMain)
            {
                playerChar = PlayerCharacter.Player.transform;
                //MelonLogger.Msg((counter++).ToString());

                Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);//Quaternion.AngleAxis(180, Vector3.up);
                Player.instance.transform.position = new(0.65f, 0, 3.55f);
                //MelonLogger.Msg((counter++).ToString());

                if (inGameMain && PlayerCharacter.Player is not null)
                {
                    RemovePlayerHead();
                }
                //MelonLogger.Msg((counter++).ToString());

                Player.instance.leftHand.useHoverSphere = true;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = false;
                Player.instance.rightHand.useHoverSphere = true;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = false;
                //MelonLogger.Msg((counter++).ToString());

                PlayerCharacter.add_OnPlayerLateStart(new Action(() => GameMainLateStart()));
                //MelonLogger.Msg((counter++).ToString());
                var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
                cinemachineBrain.enabled = false;
                //MelonLogger.Msg((counter++).ToString());
            }
            else if (inMainMenu)
            {
                //MelonLogger.Msg((counter++).ToString());
                Player.instance.leftHand.useHoverSphere = false;
                Player.instance.leftHand.useControllerHoverComponent = false;
                Player.instance.leftHand.useFingerJointHover = false;
                Player.instance.rightHand.useHoverSphere = false;
                Player.instance.rightHand.useControllerHoverComponent = false;
                Player.instance.rightHand.useFingerJointHover = false;
                //MelonLogger.Msg((counter++).ToString());

                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);
                Player.instance.transform.position = new(0.55f, 0, -10);
                //MelonLogger.Msg((counter++).ToString());

                //stop the camera from lerping towards the looktargets
                MainMenuCharacterCustomization.Singleton._cameraSpeedMultiplier = 0;
                //MelonLogger.Msg((counter++).ToString());

                CreateMainMenuBoundary();
                //MelonLogger.Msg((counter++).ToString());

                UIManager.UpdateUIPos = false;
                //MelonLogger.Msg((counter++).ToString());
            }
            else if (Player.instance is not null)
            {
                if (Player.instance.leftHand is not null)
                {
                    Player.instance.leftHand.useHoverSphere = false;
                    Player.instance.leftHand.useControllerHoverComponent = false;
                    Player.instance.leftHand.useFingerJointHover = false;
                    //MelonLogger.Msg((counter++).ToString());
                }
                if (Player.instance.rightHand is not null)
                {
                    Player.instance.rightHand.useHoverSphere = false;
                    Player.instance.rightHand.useControllerHoverComponent = false;
                    Player.instance.rightHand.useFingerJointHover = true;
                    //MelonLogger.Msg((counter++).ToString());
                }
                Player.instance.transform.rotation = Quaternion.Euler(0, 0, 0);
                //MelonLogger.Msg((counter++).ToString());
            }

            if (inLoadingScreen && Player.instance is not null)
            {
                var cinemachineBrain = Object.FindObjectOfType<CinemachineBrain>();
                cinemachineBrain.enabled = false;
                //MelonLogger.Msg((counter++).ToString());
                Player.instance.transform.position = new(0, 0, 3);
                Player.instance.transform.rotation = Quaternion.Euler(0, 180, 0);
                //MelonLogger.Msg((counter++).ToString());
                foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
                {
                    if (obj.name == "ScreenFade")
                    {
                        Object.DestroyImmediate(obj);
                        continue;
                    }
                    obj.Cast<Canvas>().gameObject.AddComponent<WorldSpaceOverlayUI>();
                }
                //MelonLogger.Msg((counter++).ToString());
            }

            UIManager.OnSceneChange();
            Hand.UpdateScene();
            //MelonLogger.Msg((counter++).ToString());

            if (!inGameMain && Player.instance is not null)
            {
                if (Laser.LeftLaser is not null)
                {
                    Laser.LeftLaser.LaserMask = Laser.DefaultLaserMask;
                    //MelonLogger.Msg((counter++).ToString());
                }
                if (Laser.RightLaser is not null)
                {
                    Laser.RightLaser.LaserMask = Laser.DefaultLaserMask;
                    //MelonLogger.Msg((counter++).ToString());
                }
            }

            MelonLogger.Msg("[HPVR] scene preparation done for " + sceneName);
        }

        private void GameMainLateStart()
        {
            MelonLogger.Msg("late start");

            CreateHouseBoundaryFixes();
            UpdateInteractiveItems();

            var mask = LayerMask.GetMask("Default", "UI", "InteractiveItems", "InteractiveItemsHighlighted", "Ragdolls", "Ground", "Walls");
            Laser.LeftLaser.LaserMask = mask;
            Laser.RightLaser.LaserMask = mask;

            //turn off player model and collision for now
            PlayerCharacter.Player._bodySkinnedMeshRenderer.enabled = false;
            GameObject.Find("CH_PlayerFemale")?.SetActive(false);
            GameObject.Find("CH_PlayerMale")?.SetActive(false);
            PlayerCharacter.Player.Controller.enabled = false;
            PlayerCharacter.Player._controlManager.DeactivateMovement();
            PlayerCharacter.Player.PuppetMaster.Puppet.gameObject.SetActive(false);
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
                    MelonLogger.Msg("turned on fallback fog");
                    f.gameObject.SetActive(true);
                    f.priority = 9999;
                    break;
                }
            }

            VRSystem.SyncPlayerAndHMD();

            if (VRSystem.SetUpInput)
            {
                SteamVR_Actions.default_InteractUI.onStateUp += (state, source) => TrySkipDialogue();
                SteamVR_Actions.default_InteractUI.onStateUp += (state, source) => UpdateDialogueResponses();
            }

            //foreach (var ui in UIManager.UIElements)
            //{
            //    ui.GetComponent<UIElement>().SetDebugMesh("");
            //}

            //this one might crash so we do it last
            SetUpInGameCanvas();
        }

        private void TrySkipDialogue()
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
            foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
            {
                Canvas canvas = obj.Cast<Canvas>();
                switch (canvas.gameObject.name)
                {
                    case "BGCUICanvas":
                    case "OrgasmManager":
                    case "NarrartorCanvas":
                    case "GameOverCanvas":
                    case "MiniGameCanvas":
                    case "CombatManager":
                    case "Relationship Notificatiops Canvas":
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

                    //dont add to these
                    case "GameMenuCanvas":
                    case "AudioSettingsCanvas":
                    case "GameplaySettingsCanvas":
                    case "UIRadialMenuCanvas": //uiradial = messages, opportunity window open radial
                    case "GraphicsMenuCanvas":
                    case "ConsoleCanvas":
                    case "DebugCanvas": //debug log
                    case "SaveCanvas":
                    case "LoadCanvas":
                    case "CameraView":
                    case "MadisonPhoneCanvas":
                    case "UseSelectCanvas":
                        break;
                    case "DialogueCanvas":
                        Dialogue = canvas;
                        break;
                    case "ScreenFadeCanvas":
                        canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        UIManager.CanvasToIgnore.Add(canvas.name);
                        ScreenFade = canvas;
                        break;
                    case "RadialMenuCanvas":
                        UIManager.CanvasToIgnore.Add(canvas.name);
                        RadialCanvas = canvas;
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
                        //todo add component that triggers the radialmenu canvas as an onSubmit to all buttons in the inventory, whenever that is opened
                        break;
                    case "InteractionCanvas":
                        canvas.gameObject.AddComponent<WorldSpaceOverlayUI>();
                        UIManager.CanvasToIgnore.Add(canvas.name);
                        interactionCanvas = canvas;
                        break;
                }
            }
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

            Dialogue.transform.localPosition += new Vector3(0, -400, 0);
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

        private static void OnRadialButtonSubmit(Button button)
        {
            MelonLogger.Msg($"{RadialMenu.Singleton.IsShowing} {InteractionManager.Singleton.CurrentFocusedItem?.name}");
            if (RadialMenu.Singleton.IsShowing && InteractionManager.Singleton.CurrentFocusedItem is not null)
            {
                var option = RadialMenu.Singleton.buttons.IndexOf(button);
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
                if (ScreenFade is not null)
                {
                    UpdateScreenFadeCanvas();
                }
                UpdateNarratorMessage();

                TryDisableMoveDuringDialogue();
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

            VRSystem.Update();
            UIManager.Update();
        }

        private void TryDisableMoveDuringDialogue()
        {
            if (!DialogueVisible && DialogueUI.Singleton.IsShowing)
            {
                if (itemWhenDialogueStart is null)
                {
                    itemWhenDialogueStart = InteractiveItem.ActiveItem;
                    DialogueVisible = true;
                    //MelonLogger.Msg("set item and dialogue visible");
                }
                SetUpDialogueCanvas();
            }
            //disable movement only when far enough away
            else if (DialogueVisible && DialogueUI.Singleton.IsShowing)
            {
                if (itemWhenDialogueStart is not null && VRSystem.MovementEnabled)
                {
                    if (DistanceEvaluator.EvaluateOne(itemWhenDialogueStart.gameObject, SteamVR_Camera.instance.gameObject, 2.3f, GreaterThanLessThanEquations.GreaterThan))
                    {
                        //MelonLogger.Msg("more than 2.3f away");
                        VRSystem.MovementEnabled = false;
                    }
                }
            }
            else if (DialogueVisible && !DialogueUI.Singleton.IsShowing)
            {
                itemWhenDialogueStart = null;
                DialogueVisible = false;
                VRSystem.MovementEnabled = true;
                //MelonLogger.Msg("unset item and dialogue visibility");
            }
            else if (!DialogueVisible && !DialogueUI.Singleton.IsShowing && itemWhenDialogueStart is not null)
            {
                itemWhenDialogueStart = null;
            }
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

        private void UpdateScreenFadeCanvas()
        {
            if (ScreenFade is null)
            {
                return;
            }

            if (!(Il2CppEekCharacterEngine.Interface.ScreenFade.Singleton?.IsFadeVisible ?? false))
            {
                return;
            }

            Transform camera = SteamVR_Camera.instance.transform;
            ScreenFade.transform.position = camera.position + (camera.rotation * Vector3.forward * 0.4f);

            ScreenFade.transform.LookAt(ScreenFade.transform.position - camera.position);
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
            interactionCanvas.transform.rotation = camera.rotation;
        }

        private static void UpdateHPPlayerPositiion()
        {
            if (PlayerCharacter.Player is null)
            {
                return;
            }

            PlayerCharacter.Player.transform.position = Player.instance.transform.position;
            if (PlayerCharacter.Player.Controller is not null)
            {
                PlayerCharacter.Player.Controller.enabled = false;
            }
            PlayerCharacter.Player._controlManager?.DeactivateMovement();
            PlayerCharacter.Player.PuppetMaster?.Puppet?.gameObject?.SetActive(false);
            //MelonLogger.Msg($"{PlayerCharacter.Player.transform.position.x} {PlayerCharacter.Player.transform.position.y} {PlayerCharacter.Player.transform.position.z}");
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
            if (!loading._gameLoader.allowSceneActivation && loading._loaded && loading._gameLoader.progress >= 0.9f)
            {
                if (!shownLoadingScreenInfo)
                {
                    shownLoadingScreenInfo = true;
                    MelonLogger.Msg("loading screen done!");
                }
                if (VRSystem.HandInputActive)
                {
                    MelonLogger.Msg("started loading new scene!");
                    loading._gameLoader.allowSceneActivation = true;
                }
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
                        //todo fine tune and keep out things like oven and stuff
                        //this seems to include most items for now
                        if (item.canBeGrabbed)
                        {
                            item.gameObject.AddComponent<GrabbableInteractable>();
                        }
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

        private static void ScalePlayerToHMDHeight()
        {
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
                cam.cullingMask |= LayerMask.GetMask("InvisibleToMainCamera");
            }

            //if (PlayerCharacter.Player.Gender == Genders.Female)
            //{
            //    GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
            //}
            //else
            //{
            //    GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
            //}
            //GameObject.Find("Hair_Mirror")?.SetActive(false);
            //var lEye = playerChar.FindDeepChild("lEye");
            //lEye.localScale = Vector3.zero;
            //var rEye = playerChar.FindDeepChild("rEye");
            //rEye.localScale = Vector3.zero;
            removedPlayerHead = true;
        }
    }
}