using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BerryVR
{
    /// <summary>
    /// Managed bridge to BerryVRRender.dll. Unity's D3D11 device is owned by
    /// the render thread, so OpenVR Submit is invoked from a render event
    /// (GL.IssuePluginEvent) instead of from a MonoBehaviour callback.
    /// </summary>
    internal static class VRNative
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetTexturesFn(IntPtr left, IntPtr right);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetRenderEventFuncFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetIntFn();

        private static IntPtr _module;
        private static SetTexturesFn _setTextures;
        private static GetIntFn _getSubmitCount;
        private static GetIntFn _getEventCount;
        private static GetIntFn _getLastError;

        public static bool Ready { get; private set; }
        public static IntPtr EventFunc { get; private set; }
        public static string LoadError { get; private set; }

        public static bool Init(string pluginDirectory)
        {
            if (Ready) return true;

            try
            {
                string path = Path.Combine(pluginDirectory ?? "", "BerryVRRender.dll");
                if (!File.Exists(path))
                {
                    LoadError = "missing " + path;
                    return false;
                }

                _module = LoadLibraryW(path);
                if (_module == IntPtr.Zero)
                {
                    LoadError = "LoadLibrary failed " + Marshal.GetLastWin32Error();
                    return false;
                }

                IntPtr setPtr = GetProcAddress(_module, "BerryVR_SetTextures");
                IntPtr getFuncPtr = GetProcAddress(_module, "BerryVR_GetRenderEventFunc");
                IntPtr countPtr = GetProcAddress(_module, "BerryVR_GetSubmitCount");
                IntPtr eventPtr = GetProcAddress(_module, "BerryVR_GetEventCount");
                IntPtr errorPtr = GetProcAddress(_module, "BerryVR_GetLastError");
                if (setPtr == IntPtr.Zero || getFuncPtr == IntPtr.Zero)
                {
                    LoadError = "missing export";
                    return false;
                }

                _setTextures = Marshal.GetDelegateForFunctionPointer<SetTexturesFn>(setPtr);
                var getEventFunc = Marshal.GetDelegateForFunctionPointer<GetRenderEventFuncFn>(getFuncPtr);
                EventFunc = getEventFunc();
                if (EventFunc == IntPtr.Zero)
                {
                    LoadError = "null render event function";
                    return false;
                }

                if (countPtr != IntPtr.Zero)
                    _getSubmitCount = Marshal.GetDelegateForFunctionPointer<GetIntFn>(countPtr);
                if (eventPtr != IntPtr.Zero)
                    _getEventCount = Marshal.GetDelegateForFunctionPointer<GetIntFn>(eventPtr);
                if (errorPtr != IntPtr.Zero)
                    _getLastError = Marshal.GetDelegateForFunctionPointer<GetIntFn>(errorPtr);

                Ready = true;
                LoadError = null;
                return true;
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                return false;
            }
        }

        public static void SetTextures(IntPtr left, IntPtr right)
        {
            try { _setTextures?.Invoke(left, right); }
            catch { }
        }

        public static int SubmitCount
        {
            get
            {
                try { return _getSubmitCount != null ? _getSubmitCount() : 0; }
                catch { return -1; }
            }
        }

        public static int EventCount
        {
            get
            {
                try { return _getEventCount != null ? _getEventCount() : 0; }
                catch { return -1; }
            }
        }

        public static int LastError
        {
            get
            {
                try { return _getLastError != null ? _getLastError() : 0; }
                catch { return -1; }
            }
        }
    }
}
