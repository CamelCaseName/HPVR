using Il2Cpp;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Motion;
using Il2CppMono.Globalization.Unicode;
using MelonLoader;
using SteamVR_Melon.Standalone;
using SteamXR_Melon;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityEngine.InputSystem;
using Valve.VR;
using Object = UnityEngine.Object;
using Resources = HPVR.Properties.Resources;

namespace HPVR
{

    public class HPVR : MelonMod
    {
        private bool usingVrCamera = false;
        private bool inGameMain = false;
        private Quaternion vrCamRotation = Quaternion.identity;
        private Quaternion vrCamRotationStart = Quaternion.AngleAxis(180, Vector3.up);
        private float vrControllerRotation = 0.0f;
        private Vector3 vrControllerPosition = new();
        private Vector3 vrCamPosition = new(0, 1.75f, 0);
        private Vector3 vrCamPositionStart = new();
        private Vector3 vrCamVelocity = new();
        private Vector3 hmdPositionDelta = new();

        private GameObject? Playerneck = null;

        private Vector2 trackpad;

        public SteamVR_Input_Sources Hand;//Set Hand To Get Input From
        public float speed = 20f;
        public CapsuleCollider Collider;
        public float Deadzone = 0.0f;//the Deadzone of the trackpad. used to prevent unwanted walking.

        #region dirtyStuff

        static HPVR()
        {
            //MelonLogger.Msg("Static init");
            SetOurResolveHandlerAtFront();
        }
        private static Assembly AssemblyResolveEventListener(object sender, ResolveEventArgs args)
        {
            if (args is null)
            {
                return null!;
            }

            string dllName = args.Name[..args.Name.IndexOf(',')];
            var name = "HPVR.Resources.resources" + dllName + ".dll";
            string path = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "UserLibs", dllName + ".dll");
            foreach (var field in typeof(Resources).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {

                if (field.Name == dllName.Replace('.', '_'))
                {
                    var context = new AssemblyLoadContext(name, false);
                    MelonLogger.Warning($"Loaded {args.Name} from our embedded resources, saving to userlibs for next time");
                    File.WriteAllBytes(path, (byte[])field.GetValue(null)!);
                    Stream s = File.OpenRead(path);
                    var asm = context.LoadFromStream(s);
                    s.Close();
                    return asm;
                }
            }
            //MelonLogger.Error($"{args.Name} not found in resources");
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
                foreach (var field in typeof(Resources).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {

                    if (field.Name == "actions" || field.Name.StartsWith("bindings_") || field.Name.StartsWith("binding_"))
                    {
                        File.WriteAllBytes(Path.Combine(folderPath, field.Name + ".json"), (byte[])field.GetValue(null)!);
                        Stream s = File.OpenRead(Path.Combine(folderPath, field.Name + ".json"));
                        s.Close();
                        MelonLogger.Warning($"Loaded {field.Name}.json from our embedded resources, saving for next time");
                    }
                }
            }

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets");
            CreateAndSaveToPath(folderPath, "vrshaders", "");
            CreateAndSaveToPath(folderPath, "vrshaders1", ".manifest", "vrshaders");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "UnitySubsystems", "XRSDKOpenVR");
            CreateAndSaveToPath(folderPath, "UnitySubsystemManifest", ".json");

            folderPath = Path.Combine(HousePartyMainLocation, "HouseParty_Data", "StreamingAssets", "SteamVR");
            CreateAndSaveToPath(folderPath, "OpenVRSettings", ".asset");

            static void CreateAndSavePlugin(string name)
            {
                string folderPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "Mods", "HPVR_data");
                string path = Path.Combine(folderPath, name + ".dll");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folderPath);
                    foreach (var field in typeof(Resources).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {

                        if (field.Name == name)
                        {
                            File.WriteAllBytes(path, (byte[])field.GetValue(null)!);
                            Stream s = File.OpenRead(path);
                            s.Close();
                            folderPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly()?.Location!)!.Parent!.FullName, "HouseParty_Data", "Plugins");
                            path = Path.Combine(folderPath, name + ".dll");
                            File.WriteAllBytes(path, (byte[])field.GetValue(null)!);
                            s = File.OpenRead(path);
                            s.Close();
                            MelonLogger.Warning($"Loaded {name} from our embedded resources, saving for next time");
                            return;
                        }
                    }
                }
            }

            static void CreateAndSaveToPath(string folderPath, string name, string ending, string filename = "")
            {
                if (String.IsNullOrEmpty(filename))
                    filename = name;
                string path = Path.Combine(folderPath, filename + ending);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folderPath);
                    foreach (var field in typeof(Resources).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {

                        if (field.Name == name)
                        {
                            File.WriteAllBytes(path, (byte[])field.GetValue(null)!);
                            Stream s = File.OpenRead(path);
                            s.Close();
                            MelonLogger.Warning($"Loaded {name}{ending} from our embedded resources, saving to {folderPath} for next time");
                            return;
                        }
                    }
                }
            }
        }

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

            MelonLogger.Msg("[HPVR] hpvr loading");
            MelonLogger.Msg("[HPVR] adding steamvr");
            //Camera.main.gameObject.AddComponent<SteamVR_Fade>();
            Camera.main.gameObject.AddComponent<SteamVR_Camera>();

            var eekCam = Object.FindObjectOfType<EekCamera>();
            if (eekCam is not null)
            {
                //MelonLogger.Msg("[HPVR] destroying eek camera");
                MelonLogger.Msg(eekCam);
                Object.DestroyImmediate(eekCam);
                //MelonLogger.Msg("[HPVR] main cam null? " + Camera.main is null);
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

            MelonLogger.Msg($"{SteamVR_Actions.platformer_Move.activeBinding}");

            MelonLogger.Msg("[HPVR] hpvr loaded");
            var res = SteamVR_Camera.GetSceneResolution();
            MelonLogger.Msg($"[HPVR] {res.width}:{res.height}");
        }

        private void Rotate(float rotation)
        {
            //vrControllerRotation = (vrControllerRotation + (0.11f * rotation)) % 360;
        }

        public static float Angle(Vector2 p_vector2)
        {
            if (p_vector2.x < 0)
            {
                return 360 - (Mathf.Atan2(p_vector2.x, p_vector2.y) * Mathf.Rad2Deg * -1);
            }
            else
            {
                return Mathf.Atan2(p_vector2.x, p_vector2.y) * Mathf.Rad2Deg;
            }
        }

        private void updateInput()
        {
            trackpad = SteamVR_Actions.platformer_Move.axis;
            //MelonLogger.Msg($"{trackpad.x}|{trackpad.y}");
        }

        public override void OnUpdate()
        {
            if (usingVrCamera)
            {
                float seconds = predictSecondsFromNow();
                TrackedDevicePose_t[] poses = new TrackedDevicePose_t[4];
                OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, seconds, poses);
                //MelonLogger.Msg(poses[0].ToStringDetail());
                vrCamRotation = vrCamRotationStart * poses[0].mDeviceToAbsoluteTracking.GetRotation();
                if (vrCamPositionStart.sqrMagnitude == 0)
                {
                    vrCamPositionStart = new Vector3(poses[0].mDeviceToAbsoluteTracking.GetPosition().x, 0, poses[0].mDeviceToAbsoluteTracking.GetPosition().z);
                }
                vrCamPosition = new(-poses[0].mDeviceToAbsoluteTracking.GetPosition().x - vrCamPositionStart.x, poses[0].mDeviceToAbsoluteTracking.GetPosition().y, -poses[0].mDeviceToAbsoluteTracking.GetPosition().z - vrCamPositionStart.z);
                //MelonLogger.Msg($"{poses[0].mDeviceToAbsoluteTracking.GetPosition().x}-{vrCamPositionStart.x}={vrCamPosition.x} | {poses[0].mDeviceToAbsoluteTracking.GetPosition().z}-{vrCamPositionStart.z}={vrCamPosition.z}");
                vrCamVelocity = new Vector3(poses[0].vVelocity.v0, poses[0].vVelocity.v1, poses[0].vVelocity.v2);

                if (inGameMain && SteamVR_Camera.instance?.transform is not null && PlayerCharacter.Player?.Head is not null)
                {
                    if (Playerneck is null)
                    {
                        if (PlayerCharacter.Player.Gender == Il2CppEekEvents.Genders.Female)
                        {
                            GameObject.Find("PlayerFemale_HeadMirror")?.SetActive(false);
                        }
                        else
                        {
                            GameObject.Find("PlayerMale_HeadMirror")?.SetActive(false);
                        }
                        Playerneck = PlayerCharacter.Player.transform.FindChild("neckUpper")?.gameObject;
                        Playerneck?.SetActive(false);
                    }

                    //todo add controller based snap rotation
                    vrCamRotation = Quaternion.AngleAxis(vrControllerRotation, Vector3.up) * vrCamRotation;
                    SteamVR_Camera.instance.transform.rotation = vrCamRotation;
                    PlayerCharacter.Player.transform.rotation = Quaternion.Euler(0, vrCamRotation.eulerAngles.y, 0);

                    var oldPos = SteamVR_Camera.instance.transform.position;
                    SteamVR_Camera.instance.transform.position = PlayerCharacter.Player.transform.position + vrCamPosition;
                    //todo add real hmd xyz movement here as well somehow. maybe force player to crouch after some point?
                    //maybe we can force the player to IK to some height? like stretch them outside the range crouching gives us

                    var moveDirectionForward = PlayerCharacter.Player.transform.rotation * Vector3.forward;//get the angle of the touch and correct it for the rotation of the controller
                    var moveDirectionSide = PlayerCharacter.Player.transform.rotation * Vector3.right;//get the angle of the touch and correct it for the rotation of the controller
                    updateInput();

                    if (PlayerCharacter.Player.CurrentMovementSpeed < 1f && trackpad.magnitude > Deadzone)
                    {//make sure the touch isn't in the deadzone and we aren't going to fast.
                        hmdPositionDelta = new Vector3(PlayerCharacter.Player.transform.position.x - SteamVR_Camera.instance.transform.position.x, 0, PlayerCharacter.Player.transform.position.z - SteamVR_Camera.instance.transform.position.z);
                        //MelonLogger.Msg($"{trackpad.x} : {trackpad.y}");
                        var joystickdelta = (moveDirectionForward * trackpad.y * (trackpad.sqrMagnitude / speed))
                            + (moveDirectionSide * trackpad.x * (trackpad.sqrMagnitude / speed));
                        //MelonLogger.Msg($"{hmdPositionDelta.x} : {hmdPositionDelta.z}    {joystickdelta.x} {joystickdelta.z}");

                        if (hmdPositionDelta.sqrMagnitude > 0.3f)
                        {
                            var sum = joystickdelta + (hmdPositionDelta * Time.deltaTime);
                            PlayerCharacter.Player.Controller.Move_Injected(ref sum);
                            hmdPositionDelta = Vector3.zero;
                        }
                        else
                        {
                            PlayerCharacter.Player.Controller.Move_Injected(ref joystickdelta);
                            //PlayerCharacter.Player._controlManager._move.
                        }

                        //MelonLogger.Msg($"player root{PlayerCharacter.Player.PlayerRootTransform.position.x}|{PlayerCharacter.Player.PlayerRootTransform.position.y}|{PlayerCharacter.Player.PlayerRootTransform.position.z}    cam pos{SteamVR_Camera.instance.transform.position.x}|{SteamVR_Camera.instance.transform.position.y}|{SteamVR_Camera.instance.transform.position.z}");
                    }
                }
            }
            //if (Camera.main is null)
            //{
            //    MelonLogger.Msg("main camera was set to null!");
            //}

            //if (Keyboard.current.qKey.wasPressedThisFrame)
            //{
            //    SteamVR_Camera.DumpRenderTexture();
            //}
        }

        private float predictSecondsFromNow()
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

        public override void OnLateUpdate()
        {
            if (usingVrCamera && inGameMain && SteamVR_Camera.instance?.transform is not null && PlayerCharacter.Player?.Head is not null)
            {
            }
        }
    }

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
}