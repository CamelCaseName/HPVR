using Il2Cpp;
using MelonLoader;
using SteamVR_Melon.Standalone;
using SteamXR_Melon;
using System.Reflection;
using System.Runtime.Loader;
using Unity.XR.OpenVR;
using UnityEngine;
using Valve.VR;
using Object = UnityEngine.Object;
using Resources = HPVR.Properties.Resources;

namespace HPVR
{

    public class HPVR : MelonMod
    {
        #region dirtyStuff

        static HPVR()
        {
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
            MelonXR.Initialize();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName is not ("GameMain" or "MainMenu"))
            {
                return;
            }
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
            MelonLogger.Msg("[HPVR] hpvr loaded");
            var res = SteamVR_Camera.GetSceneResolution();
            MelonLogger.Msg($"[HPVR] {res.width}:{res.height}");
        }

        public override void OnUpdate()
        {
            //if (Camera.main is null)
            //{
            //    MelonLogger.Msg("main camera was set to null!");
            //}

            //if (Keyboard.current.qKey.wasPressedThisFrame)
            //{
            //    SteamVR_Camera.DumpRenderTexture();
            //}
        }
    }
}