using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekEvents.Helper;
using MelonLoader;
using SteamXR_Melon;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
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
        //public CapsuleCollider Collider;
        public float Deadzone = 0.0f;

        //public SteamVR_Input_Sources Hand;//Set Hand To Get Input From
        public float speed = 20f;

        private Vector3 hmdPositionDelta = new();
        private bool inGameMain = false;
        private GameObject? Playerneck = null;
        private Vector2 trackpad;
        private bool usingVrCamera = false;
        private Vector3 vrCamPosition = new(0, 1.75f, 0);
        private Vector3 vrCamPositionStart = new();
        private Quaternion vrCamRotation = Quaternion.identity;
        private Quaternion vrControllerRotation = Quaternion.identity;
        private Quaternion vrCamRotationStart = new();
        private Vector3 vrControllerPosition = new();
        private bool inTurn = false;
        private TrackedDevicePose_t[] poses = new TrackedDevicePose_t[4];

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
        }

        //TODO move the vr cam in x and z with the controller, rotation as well. then just set the player to move to where the head is and rotate the player the same way the cam is

        public override void OnInitializeMelon()
        {
            RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(SteamVR)));
            RegisterTypeInIl2Cpp.RegisterAssembly(Assembly.GetAssembly(typeof(MelonXR)));
            //UnityEngine.Rendering.TextureXR.maxViews = 2;
            //do steamvr before melonxr
            SteamVR.Initialize(false);
            if (SteamVR.instance is null)
            {
                this.Unregister("VR Headset was not connected before starting the game", false);
            }
            MelonXR.Initialize();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName is not ("GameMain" or "MainMenu"))
            {
                usingVrCamera = false;
                return;
            }
            usingVrCamera = true;
            inGameMain = sceneName == "GameMain";

            if (inGameMain)
            {
                vrControllerRotation = Quaternion.Euler(0, 0, 0);//Quaternion.AngleAxis(180, Vector3.up);
                vrControllerPosition = new(0.7f, 0, 3.55f);
            }
            else
            {
                vrControllerRotation = Quaternion.Euler(355, 0, 0);
                vrControllerPosition = new(0.55f, 0.6635f, -10);
            }
            MelonLogger.Msg($"controller start rot ({vrControllerRotation.eulerAngles.x}|{vrControllerRotation.eulerAngles.y}|{vrControllerRotation.eulerAngles.z}) start pos ({vrControllerPosition.x}|{vrControllerPosition.y}|{vrControllerPosition.z})");

            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
            Vector3 hmdabsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();
            vrCamPositionStart = new Vector3(hmdabsolutePosition.x, 0, hmdabsolutePosition.z);
            vrCamRotationStart = Quaternion.Inverse(poses[0].mDeviceToAbsoluteTracking.GetRotation());
            MelonLogger.Msg($"hmd start rot ({vrCamRotationStart.eulerAngles.x}|{vrCamRotationStart.eulerAngles.y}|{vrCamRotationStart.eulerAngles.z}) start pos ({vrCamPositionStart.x}|{vrCamPositionStart.y}|{vrCamPositionStart.z})");

            MelonLogger.Msg("[HPVR] hpvr loading");
            MelonLogger.Msg("[HPVR] adding steamvr");
            //Camera.main.gameObject.AddComponent<SteamVR_Fade>();
            Camera.main.gameObject.AddComponent<SteamVR_Camera>();

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                Object.DestroyImmediate(eekCam);
            }

            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                cam.cullingMask |= LayerMask.GetMask("InvisibleToMainCamera");
            }

            if (inGameMain && PlayerCharacter.Player is not null)
            {
                GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
                GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
                PlayerCharacter.Player.transform.FindChild("neckUpper")?.gameObject?.SetActive(false);
            }

            SteamVR_Actions._default.Activate();
            SteamVR_Actions.platformer_Move.actionSet.Activate(priority: 1);

            MelonLogger.Msg("[HPVR] hpvr loaded");
            var res = SteamVR_Camera.GetSceneResolution();
            MelonLogger.Msg($"[HPVR] {res.width}:{res.height}");
        }

        public override void OnUpdate()
        {
            if (!usingVrCamera)
            {
                return;
            }

            UpdateInput();
            UpdateHMDPositions();

            if (!inGameMain || SteamVR_Camera.instance?.transform is null || PlayerCharacter.Player?.Head is null)
            {
                return;
            }

            //todo maybe we can force the player to IK to some height? like stretch them outside the range crouching gives us (sizing player to fit the height between crouch and stuff)
            //Transform cameraTransform = SteamVR_Camera.instance.transform;
            //PlayerCharacter.Player.transform.rotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);
            //hmdPositionDelta = new Vector3(cameraTransform.position.x - PlayerCharacter.Player.transform.position.x, 0, cameraTransform.position.z - PlayerCharacter.Player.transform.position.z);
            //PlayerCharacter.Player.Controller.Move_Injected(ref hmdPositionDelta);
            ////todo detect when the player is colliding with a wall (position differnce or collision flags) then stop controller movement and only resume it once no longer colliding


            //if (Playerneck is null)
            //{
            //    if (PlayerCharacter.Player.Gender == Il2CppEekEvents.Genders.Female)
            //    {
            //        GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
            //    }
            //    else
            //    {
            //        GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
            //    }
            //    Playerneck = PlayerCharacter.Player.transform.FindDeepChild("neckUpper")?.gameObject;
            //    Playerneck?.SetActive(false);
            //}
        }

        private void UpdateHMDPositions()
        {
            float seconds = PredictSecondsFromNow();
            TrackedDevicePose_t[] poses = new TrackedDevicePose_t[4];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
            Vector3 hmdabsolutePosition = poses[0].mDeviceToAbsoluteTracking.GetPosition();

            //this is fine
            if (inGameMain && PlayerCharacter.Player != null)
            {
                hmdabsolutePosition.y += PlayerCharacter.Player.transform.position.y;
            }

            vrCamRotation = vrControllerRotation * (poses[0].mDeviceToAbsoluteTracking.GetRotation() * vrCamRotationStart);
            vrCamPosition = vrControllerPosition + vrControllerRotation * (hmdabsolutePosition - vrCamPositionStart);

            MelonLogger.Msg($"update hmd: ({vrCamRotation.eulerAngles.x}|{vrCamRotation.eulerAngles.y}|{vrCamRotation.eulerAngles.z}) act pos ({vrCamPosition.x}|{vrCamPosition.y}|{vrCamPosition.z})");

            SteamVR_Camera.instance.transform.rotation = vrCamRotation;
            SteamVR_Camera.instance.transform.position = vrCamPosition;
        }

        static void CreateAndSavePlugin(string name)
        {
            string folderPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "HouseParty_Data", "Plugins", "x86_64");
            string path = Path.Combine(folderPath, name + ".dll");
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

        private float PredictSecondsFromNow()
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

        private void UpdateInput()
        {
            if (!inGameMain)
            {
                return;
            }

            trackpad = SteamVR_Actions.platformer_Move.axis;

            if (inGameMain && trackpad.magnitude > Deadzone)
            {
                var yRotation = Quaternion.Euler(0, vrCamRotation.eulerAngles.y, 0);
                var moveDirectionForward = yRotation * Vector3.forward;//get the angle of the touch and correct it for the rotation of the controller
                var moveDirectionSide = yRotation * Vector3.right;//get the angle of the touch and correct it for the rotation of the controller

                vrControllerPosition += (moveDirectionForward * trackpad.y * (trackpad.sqrMagnitude / speed))
                    + (moveDirectionSide * trackpad.x * (trackpad.sqrMagnitude / speed));
            }

            float snapTurn = SteamVR_Actions.default_SnapTurnLeft.state ? -1f : SteamVR_Actions.default_SnapTurnRight.state ? 1f : 0;

            //only turn once
            if (!inTurn && snapTurn != 0)
            {
                vrControllerRotation *= Quaternion.AngleAxis(snapTurn * 45, Vector3.up);
            }

            inTurn = snapTurn != 0;

            MelonLogger.Msg($"input: ({vrControllerRotation.eulerAngles.x}|{vrControllerRotation.eulerAngles.y}|{vrControllerRotation.eulerAngles.z}) start pos ({vrControllerPosition.x}|{vrControllerPosition.y}|{vrControllerPosition.z})");
        }
    }
}