using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace HPVR.utils
{
    public static class Extensions
    {
        public static string Beautify(this TrackedDevicePoseT[] values, string seperator = ", ")
        {
            StringBuilder stringBuilder = new(values.Length * 3);

            foreach (TrackedDevicePoseT value in values)
            {
                stringBuilder.Append(value.ToStringDetail() ?? null);
                stringBuilder.Append(seperator);
            }
            stringBuilder.Remove(stringBuilder.Length - 3, 2);

            return stringBuilder.ToString();
        }

        public static string ToStringDetail(this TrackedDevicePoseT data)
        {
            var pos = data.mDeviceToAbsoluteTracking.GetPosition();
            var rot = data.mDeviceToAbsoluteTracking.GetRotation().eulerAngles;
            var vel = data.vVelocity;
            var ang = data.vAngularVelocity;
            return $"pos: x{pos.x} y{pos.y} z{pos.z} |rot: x{rot.x} y{rot.y} z{rot.z} |vel: x{vel.v0} y{vel.v1} z{vel.v2} |ang: x{ang.v0} y{ang.v1} z{ang.v2}";
        }

        public static void MoveContents(this Canvas canvas, Vector3 moveBy)
        {
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                var child = canvas.transform.GetChild(i);
                child.localPosition += moveBy;
            }
        }

        private static Dictionary<int, IntPtr> Allocations = new();

        public static void FreeIl2CppArray<T>(this T[] incoming) where T : struct
        {
            //var t = typeof(T);
            //if (!t.IsLayoutSequential)
            //{
            //    throw new InvalidDataException($"{t.Name} is not a sequential struct. It has to be sequential for this to work");
            //}
            //if (Allocations.TryGetValue(incoming.GetHashCode(), out IntPtr pointer))
            //{
            //    Marshal.FreeHGlobal(pointer);
            //}
            //else
            //{
            //    //throw new InvalidDataException($" the given Array of type {t.Name} was already freed or its hashcode changed before freeing!");
            //    MelonLogger.Msg($" the given Array of type {t.Name} was already freed or its hashcode changed before freeing!");
            //}
        }

        public unsafe static Il2CppSystem.Array AllocIl2CppArray<T>(this T[] incoming) where T : struct
        {
            var t = typeof(T);
            if (!t.IsLayoutSequential)
            {
                throw new InvalidDataException($"{t.Name} is not a sequential struct. It has to be sequential for this to work");
            }

            var DataSize = Unsafe.SizeOf<T>();
            DataSize *= incoming.Length;
            int ArrayObjSize = Unsafe.SizeOf<T[]>();

            //array in c# memory is 4 byte lenght, 4 byte padding and then the data
            GCHandle gC = GCHandle.Alloc(incoming, GCHandleType.Pinned);
            IntPtr pinnedArr = gC.AddrOfPinnedObject();

            //we create the array for bytes, but then copy in the real data. this should be fine?
            var array = new Il2CppStructArray<byte>(DataSize);
            var handle = IL2CPP.il2cpp_gchandle_new(array.Pointer, true);

            Memmove((void*)(array.Pointer + ArrayObjSize), (void*)(pinnedArr + ArrayObjSize), (nuint)DataSize);

            IL2CPP.il2cpp_gchandle_free(handle);
            gC.Free();

            return array.Cast<Il2CppSystem.Array>();
        }

        private unsafe static void Memmove(void* dest, void* src, nuint len)
        {
            _ = Unsafe.ReadUnaligned<byte>(dest);
            _ = Unsafe.ReadUnaligned<byte>(src);
            System.Buffer.MemoryCopy(dest, src, len, len);
        }

        public static void SetHandlerAtFront(this Action action, Delegate @delegate)
        {
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

    internal class RawArrayData
    {
        public uint Length; // Array._numComponents padded to IntPtr
        public uint Padding;
        public byte Data;
    }
}
