// Native OpenVR submission bridge for BerryVR.
//
// Unity's D3D11 device is owned by the render thread.  Calling
// IVRCompositor::Submit from a MonoBehaviour (main thread) makes the OpenVR
// compositor client touch that device from the wrong thread and can deadlock
// the whole player.  This tiny plugin is invoked through GL.IssuePluginEvent
// so Submit runs on Unity's render thread, immediately after the eye cameras
// have been queued and before the frame is presented.

#include <windows.h>
#include <stdio.h>
#include <stdarg.h>
#include "openvr.h"

static volatile PVOID g_leftTexture = nullptr;
static volatile PVOID g_rightTexture = nullptr;
static volatile LONG g_submitCount = 0;
static volatile LONG g_eventCount = 0;
static volatile LONG g_lastError = 0;

static vr::IVRCompositor* g_compositor = nullptr;
static HMODULE g_openvrModule = nullptr;

static void NativeLog(const char* format, ...)
{
    char buffer[512];
    va_list args;
    va_start(args, format);
    vsnprintf_s(buffer, sizeof(buffer), _TRUNCATE, format, args);
    va_end(args);

    FILE* file = nullptr;
    if (fopen_s(&file, "BerryVRRender.log", "a") == 0 && file != nullptr)
    {
        fputs(buffer, file);
        fputc('\n', file);
        fclose(file);
    }
}

static vr::IVRCompositor* GetCompositor()
{
    if (g_compositor != nullptr)
        return g_compositor;

    if (g_openvrModule == nullptr)
    {
        g_openvrModule = GetModuleHandleA("openvr_api.dll");
        if (g_openvrModule == nullptr)
            g_openvrModule = LoadLibraryA("openvr_api.dll");
    }
    if (g_openvrModule == nullptr)
        return nullptr;

    typedef void* (VR_CALLTYPE *PFN_VR_GetGenericInterface)(const char*, vr::EVRInitError*);
    PFN_VR_GetGenericInterface getGeneric =
        (PFN_VR_GetGenericInterface)GetProcAddress(g_openvrModule, "VR_GetGenericInterface");
    if (getGeneric == nullptr)
        return nullptr;

    vr::EVRInitError error = vr::VRInitError_None;
    g_compositor = (vr::IVRCompositor*)getGeneric(vr::IVRCompositor_Version, &error);
    NativeLog("GetGenericInterface: compositor=%p error=%d", g_compositor, (int)error);
    return g_compositor;
}

static void SubmitBothEyes()
{
    vr::IVRCompositor* compositor = GetCompositor();
    if (compositor == nullptr)
        return;

    void* left = (void*)g_leftTexture;
    void* right = (void*)g_rightTexture;
    if (left == nullptr || right == nullptr)
        return;

    vr::Texture_t texture = {};
    texture.eType = vr::TextureType_DirectX;
    texture.eColorSpace = vr::ColorSpace_Gamma;

    // Unity renders into D3D11 RenderTextures bottom-up (OpenGL-style UVs),
    // while the compositor samples them top-down. Flip V when submitting or
    // the whole view is upside down and head pitch looks inverted.
    vr::VRTextureBounds_t bounds = { 0.0f, 1.0f, 1.0f, 0.0f };

    LONG count = g_eventCount;
    bool verbose = count <= 20 || (count % 100) == 0;

    if (verbose)
        NativeLog("Submit left enter handle=%p", left);
    texture.handle = left;
    vr::EVRCompositorError leftError =
        compositor->Submit(vr::Eye_Left, &texture, &bounds, vr::Submit_Default);
    if (verbose || leftError != vr::VRCompositorError_None)
        NativeLog("Submit left done err=%d", (int)leftError);

    if (verbose)
        NativeLog("Submit right enter handle=%p", right);
    texture.handle = right;
    vr::EVRCompositorError rightError =
        compositor->Submit(vr::Eye_Right, &texture, &bounds, vr::Submit_Default);
    if (verbose || rightError != vr::VRCompositorError_None)
        NativeLog("Submit right done err=%d", (int)rightError);

    g_lastError = (LONG)((leftError != vr::VRCompositorError_None) ? leftError : rightError);
    InterlockedIncrement(&g_submitCount);
}

extern "C" __declspec(dllexport) void BerryVR_SetTextures(void* left, void* right)
{
    // The RenderTextures live for the entire VR session, so these pointers are
    // stable.  Plain aligned pointer writes are atomic on x64.
    g_leftTexture = left;
    g_rightTexture = right;
}

// Unity calls this exported function on its render thread when it reaches the
// GL.IssuePluginEvent command in the graphics queue.
extern "C" __declspec(dllexport) void UnityRenderEvent(int eventID)
{
    if (eventID == 1)
    {
        LONG count = InterlockedIncrement(&g_eventCount);
        bool verbose = count <= 20 || (count % 100) == 0;
        if (verbose)
            NativeLog("UnityRenderEvent enter count=%ld", count);
        SubmitBothEyes();
        if (verbose)
            NativeLog("UnityRenderEvent leave count=%ld", count);
    }
}

extern "C" __declspec(dllexport) void* BerryVR_GetRenderEventFunc()
{
    return (void*)&UnityRenderEvent;
}

extern "C" __declspec(dllexport) LONG BerryVR_GetSubmitCount()
{
    return g_submitCount;
}

extern "C" __declspec(dllexport) LONG BerryVR_GetEventCount()
{
    return g_eventCount;
}

extern "C" __declspec(dllexport) LONG BerryVR_GetLastError()
{
    return g_lastError;
}
