using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekEvents;
using Il2CppEekEvents.Helper;
using Il2CppEekUI;
using Il2CppHouseParty;
using MelonLoader;
using SteamXR_Melon;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using UnityEngine;
using Valve.VR;
using Object = UnityEngine.Object;

namespace HPVR
{

    public static class Extensions
    {
        public static string Beautify(this TrackedDevicePose_t[] values, string seperator = ", ")
        {
            StringBuilder stringBuilder = new(values.Length * 3);

            foreach (TrackedDevicePose_t value in values)
            {
                stringBuilder.Append(value.ToStringDetail() ?? null);
                stringBuilder.Append(seperator);
            }
            stringBuilder.Remove(stringBuilder.Length - 3, 2);

            return stringBuilder.ToString();
        }

        public static string ToStringDetail(this TrackedDevicePose_t data)
        {
            var pos = data.mDeviceToAbsoluteTracking.GetPosition();
            var rot = data.mDeviceToAbsoluteTracking.GetRotation().eulerAngles;
            var vel = data.vVelocity;
            var ang = data.vAngularVelocity;
            return $"pos: x{pos.x} y{pos.y} z{pos.z} |rot: x{rot.x} y{rot.y} z{rot.z} |vel: x{vel.v0} y{vel.v1} z{vel.v2} |ang: x{ang.v0} y{ang.v1} z{ang.v2}";
        }
    }

    public class HPVR : MelonMod
    {
        private bool colliding = false;
        private bool inGameMain = false;
        private bool inMainMenu = false;
        private bool inFade = false;
        private bool SetUpInput = false;
        private GameObject? playerEye = null;
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
        public float Deadzone = 0.0f;
        public float speed = 0.5f;
        public Transform? player;

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

            MelonLogger.Msg("[HPVR] HPVR loaded");
            var res = SteamVR_Camera.GetSceneResolution();
            MelonLogger.Msg($"[HPVR] Resolution: {res.width}:{res.height}");
        }

        private void CreateMainMenuBoundary()
        {
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

        //todo add colliders on the outside of the floor collider in the main menu
        //todo put all canvasses in worlspace, regular transform scale down in front of the camera -> translate raycast from 
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
                    SteamVR_Fade.View(Color.clear, 0.3f);
                }
                if (inGameMain)
                {
                    if (!DialogueUI.Singleton.IsShowing)
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
                SteamVR_Fade.View(Color.black, 0.3f);
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

        static void CreateAndSaveToPath(string folderPath, string name, string ending, string filename = "")
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
                }
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