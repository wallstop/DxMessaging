namespace DxMessaging.Unity.Networking
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using UnityEngine;

    internal static class SerializationUtils
    {
        public static byte[] GetBytes<T>(T obj)
        {
            int size = Marshal.SizeOf(obj);
            var arr = new byte[size];
            var h = default(GCHandle);

            try
            {
                h = GCHandle.Alloc(arr, GCHandleType.Pinned);

                Marshal.StructureToPtr(obj, h.AddrOfPinnedObject(), false);
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
            finally
            {
                if (h.IsAllocated)
                {
                    h.Free();
                }
            }

            return arr;
        }

        public static T FromBytes<T>(byte[] arr) where T : struct
        {
            T data;
            var h = default(GCHandle);

            try
            {
                h = GCHandle.Alloc(arr, GCHandleType.Pinned);

                data = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());

            }
            finally
            {
                if (h.IsAllocated)
                {
                    h.Free();
                }
            }

            return data;
        }
    }
}
