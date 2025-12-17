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
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR
{
    public class HPVR : MelonMod
    {
        public static HPVR? Instance { get; private set; }
        private bool colliding = false;
        public bool inGameMain = false;
        public bool inMainMenu = false;
        public bool inLoadingScreen;
        public bool inDisclaimer;
        private bool removedPlayerHead = false;
        private readonly List<BoxCollider> colliders = new(5);
        static private Vector3 hmdVsPlayer = new();
        public Transform? playerChar;
        private bool boundPlayerHands;
        private Canvas? interactionCanvas;

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
        //##    - dialogue in game needs to work
        //##    - bind controllers to all actions needed to play through the game, so 
        //##        - Inventory, memories and Opportunity with the Q radial
        //##        - Game Menu
        //##        - E Radial

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
            VRSystem.SetUpSteamVRUnity();

            UIManager.Initialize();

            MelonLogger.Msg("[HPVR] preparing scene");
            removedPlayerHead = false;
            inGameMain = sceneName == "GameMain";
            inMainMenu = sceneName == "MainMenu";
            inLoadingScreen = sceneName == "LoadingScreen";
            inDisclaimer = sceneName == "Disclaimer";

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

                SetUpInGameCanvas();
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
            else
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

            MelonLogger.Msg("[HPVR] scene preparation done");
        }

        private void SetUpInGameCanvas()
        {
            var interaction = GameObject.Find("InteractionCanvas");
            UIManager.CanvasToIgnore.Add(interaction.transform);
            interactionCanvas = interaction.GetComponent<Canvas>();
            //todo only do for some types ui, namely the ones that always show and interaciton target
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

        public override void OnUpdate()
        {
            if (SteamVR_Camera.instance?.transform is null)
            {
                return;
            }

            UpdatePlayerCollision();

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

                if (interactionCanvas is not null)
                {
                    UpdateInteractionCanvas();
                }

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

            VRSystem.Update();
            UIManager.Update();
        }

        private void UpdateInteractionCanvas()
        {
            Transform camera = SteamVR_Camera.instance.transform;
            if (Laser.LastHit != Vector3.zero)
            {
                interactionCanvas!.transform.position = Laser.LastHit + camera.rotation * Vector3.forward * -0.05f;
            }
            else
            {
                interactionCanvas!.transform.position = camera.position + (camera.rotation * Vector3.forward * 1.45f);
            }
        }

        private void UpdatePlayerCollision()
        {
            if (inGameMain && playerChar is not null)
            {
                //Transform cameraTransform = SteamVR_Camera.instance.transform;
                //playerChar.rotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);

                //hmdVsPlayer = new Vector3(cameraTransform.position.x - playerChar.position.x, 0, cameraTransform.position.z - playerChar.position.z)/* + ((playerChar.rotation * Vector3.back) * 0.1f)*/;

                //colliding = (((int)PlayerCharacter.Player.Controller.Move_Injected(ref hmdVsPlayer)) & 1) == 1;
            }
            else if (inMainMenu)
            {
                foreach (var collider in colliders)
                {
                    colliding = collider.bounds.Contains(SteamVR_Camera.instance.transform.position);
                    if (colliding)
                    {
                        MelonLogger.Msg("colldigin");
                        break;
                    }
                }
            }
            else
            {
                colliding = false;
            }
            VRSystem.MovementEnabled = !colliding;
        }

        private void TryEndDisclaimerScreen()
        {
            var disclaimer = Object.FindObjectOfType<DisclaimerManager>();
            if (!disclaimer._shouldProcessSceneTransition && !disclaimer._loadedNextScene && VRSystem.HandInputActive)
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