using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Valve.VR;

namespace HPVR.utils
{
    public static class Extensions
    {
        public static void MoveContents(this Canvas canvas, Vector3 moveBy)
        {
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                var child = canvas.transform.GetChild(i);
                child.localPosition += moveBy;
            }
        }

        public static void SaveRT(RenderTexture rt, string name)
        {
            if (rt is null)
            {
                return;
            }
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new(rt.width, rt.height, TextureFormat.RGB24, false)
            {
                name = name
            };
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = old;
            var bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes("./image/" + name, bytes);
        }

        public unsafe static Il2CppSystem.Array AllocIl2CppArray<T>(this T[] incoming) where T : struct
        {
            var t = typeof(T);
            if (!t.IsLayoutSequential)
            {
                throw new InvalidDataException($"{t.Name} is not a sequential struct. It has to be sequential for this to work");
            }

            var structSize = Unsafe.SizeOf<T>();
            var DataSize = structSize * incoming.Length;
            int ArrayObjSize = Unsafe.SizeOf<T[]>();

            //we create the array for bytes, but then copy in the real data. this should be fine?
            var array = new Il2CppStructArray<byte>(DataSize);
            var handle = IL2CPP.il2cpp_gchandle_new(array.Pointer, true);
            byte[] byteArray = new byte[structSize];

            for (int i = 0; i < incoming.Length; i++)
            {
                fixed (T* fixedT = &incoming[0])
                fixed (byte* pBytes = byteArray)
                {
                    // Copy struct data directly to byte array using pointers
                    Buffer.MemoryCopy(fixedT, pBytes, structSize, structSize);
                }
                for (int j = 0; j < byteArray.Length; j++)
                {
                    array[i * structSize + j] = byteArray[j];
                }
            }

            IL2CPP.il2cpp_gchandle_free(handle);

            return array.Cast<Il2CppSystem.Array>();
        }

        public static void SetHandlerAtFront(this Action action, Delegate @delegate)
        {
            if (action is null)
            {
                action = (Action)@delegate;
                return;
            }

            FieldInfo invocationList = typeof(MulticastDelegate).GetField("_invocationList", BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo invocationCount = typeof(MulticastDelegate).GetField("_invocationCount", BindingFlags.NonPublic | BindingFlags.Instance)!;

            Delegate[] subscribers = action.GetInvocationList();

            Delegate currentDelegate = action;
            for (int i = 0; i < subscribers.Length; i++)
            {
                currentDelegate = Delegate.RemoveAll(currentDelegate, subscribers[i])!;
            }

            Delegate[] newSubscriptions = new Delegate[subscribers.Length + 1];
            newSubscriptions[0] = @delegate!;
            Array.Copy(subscribers, 0, newSubscriptions, 1, subscribers.Length);

            invocationList.SetValue(action, newSubscriptions);
            invocationCount.SetValue(action, (IntPtr)newSubscriptions.Length);
        }
    }
}
