using MelonLoader;
using System.Reflection;
using UnityEngine;
using YamlDotNet.Serialization;

namespace HPVR.utils
{
    internal static class Il2CppHelper
    {
        public static void CreateAndSavePlugin(string name)
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

        public static string CreateAndSaveToPath(string folderPath, string name, string ending, string filename = "")
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

        public static bool MakeBehaviour<T>(string filePath, out T? asset) where T : MonoBehaviour
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
    }
}