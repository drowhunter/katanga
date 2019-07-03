﻿using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

using Nektra.Deviare2;
using System.Diagnostics;
using Valve.VR;

public class LaunchAndPlay : MonoBehaviour
{
    string katanga_directory;

    // Absolute file path to the executable of the game. We use this path to start the game.
    string gamePath;

    // User friendly name of the game. This is shown on the big screen as info on launch.
    private string gameTitle;

    static NktSpyMgr _spyMgr;
    static NktProcess _gameProcess = null;
    static string _nativeDLLName = null;

    // Primary Texture received from game as shared ID3D11ShaderResourceView
    // It automatically updates as the injected DLL copies the bits into the
    // shared resource.
    Texture2D _bothEyes = null;
    System.Int32 gGameSharedHandle = 0;

    // Original grey texture for the screen at launch, used again for resolution changes.
    public Renderer screen;
    Material screenMaterial;
    Texture greyTexture;

    //    System.Int32 _gameEventSignal = 0;
    static int ResetEvent = 0;
    static int SetEvent = 1;

    public Text infoText;
    public Text qualityText;

    // We have to use a low level Keyyboard listener because Unity's built in listener doesn't 
    // detect keyboard events when the Unity app isn't in the foreground
    private LowLevelKeyboardListener _listener;

    // -----------------------------------------------------------------------------

    // We jump out to the native C++ to open the file selection box.  There might be a
    // way to do it here in Unity, but the Mono runtime is old and creaky, and does not
    // support modern .Net, so I'm leaving it over there in C++ land.
    [DllImport("UnityNativePlugin64")]
    static extern void SelectGameDialog([MarshalAs(UnmanagedType.LPWStr)] StringBuilder unicodeFileName, int len);

    // The very first thing that happens for the app.
    private void Awake()
    {
        // Set our FatalExit as the handler for exceptions so that we get usable 
        // information from the users.
        Application.logMessageReceived += FatalExit;

        // We need to save and restore to this Katanga directory, or Unity editor gets super mad.
        katanga_directory = Environment.CurrentDirectory;


        // Check if the CmdLine arguments include a game path to be launched.
        // We are using --game-path to make it clearly different than Unity arguments.
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            print(args[i]);
            if (args[i] == "--game-path")
            {
                gamePath = args[i + 1];
            }
            else if (args[i] == "--game-title")
            {
                gameTitle = args[i + 1];
            }
        }

        // If they didn't pass a --game-path argument, then bring up the GetOpenFileName
        // dialog to let them choose.
        if (String.IsNullOrEmpty(gamePath))
        {
            // Ask user to select the game to run in virtual 3D.  
            // We are doing this super early because there are scenarios where Unity
            // has been crashing out because the working directory changes in GetOpenFileName.

            int MAX_PATH = 260;
            StringBuilder sb = new StringBuilder("", MAX_PATH);
            SelectGameDialog(sb, sb.Capacity);

            if (sb.Length != 0)
                gamePath = sb.ToString();
        }

        if (String.IsNullOrEmpty(gamePath))
            throw new Exception("No game specified to launch.");

        // If game title wasn't passed via cmd argument then take the name of the game exe as the title instead
        if (String.IsNullOrEmpty(gameTitle))
        {
            gameTitle = gamePath.Substring(gamePath.LastIndexOf('\\') + 1);
        }

        // With the game properly selected, add name to the big screen as info on launch.
        infoText.text = "Launching...\n\n" + gameTitle;

        // Set the Quality level text on the floor to match whatever we start with.
        qualityText.text = "Quality: " + QualitySettings.names[QualitySettings.GetQualityLevel()];
    }

    // -----------------------------------------------------------------------------

    // We need to allow Recenter, even for room-scale, because people ask for it. 
    // The usual Recenter does not work for room-scale because the assumption is that
    // you will simply rotate to see.  This following code sequence works in all cases.
    // https://forum.unity.com/threads/openvr-how-to-reset-camera-properly.417509/#post-2792972

    public Transform vrCamera;
    public Transform player;

    private void RecenterHMD()
    {
        //ROTATION
        // Get current head heading in scene (y-only, to avoid tilting the floor)
        float offsetAngle = vrCamera.rotation.eulerAngles.y;
        // Now rotate CameraRig in opposite direction to compensate
        player.Rotate(0f, -offsetAngle, 0f);

        // Let's rotate the floor itself back, so that it remains stable and
        // matches their play space.  We have to use the unintuitive Z direction here, 
        // because the floor is rotated 90 degrees in X already.
        floor.transform.Rotate(0f, 0f, offsetAngle);

        //POSITION
        // Calculate postional offset between CameraRig and Camera
        //        Vector3 offsetPos = steamCamera.position - cameraRig.position;
        // Reposition CameraRig to desired position minus offset
        //        cameraRig.position = (desiredHeadPos.position - offsetPos);
    }


    // We'll also handle the Right Controller Grip action as a RecenterHMD command.

    public SteamVR_Action_Boolean recenterAction;

    private void OnRecenterAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            RecenterHMD();
    }

    // Hide the floor on center click of left trackpad. Toggle on/off.
    // Creating our own Toggle here, because the touchpad is setup as d-pad and center 
    // cannot be toggle by itself.

    public SteamVR_Action_Boolean hideFloorAction;
    public GameObject floor;
    private bool hidden = false;

    private void OnHideFloorAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource)
    {
        if (hidden)
        {
            floor.SetActive(true);
            hidden = false;
        }
        else
        {
            floor.SetActive(false);
            hidden = true;
        }
    }


    readonly int VK_F12 = 123;
    readonly int VK_LSB = 219;  // [ key
    readonly int VK_RSB = 221;  // ] key
    readonly int VK_BS = 220;   // \ key

    void _listener_OnKeyPressed(object sender, KeyPressedArgs e)
    {
        // if user pressed F12 then recenter the view of the VR headset
        if (e.KeyPressed == VK_F12)
            RecenterHMD();

        // If user presses ], let's bump the Quality to the next level and rebuild
        // the environment.  [ will lower quality setting.  Mostly AA settings.
        if (e.KeyPressed == VK_LSB)
            QualitySettings.DecreaseLevel(true);
        if (e.KeyPressed == VK_RSB)
            QualitySettings.IncreaseLevel(true);
        if (e.KeyPressed == VK_BS)
            QualitySettings.anisotropicFiltering = (QualitySettings.anisotropicFiltering == AnisotropicFiltering.Disable) ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;

        qualityText.text = "Quality: " + QualitySettings.names[QualitySettings.GetQualityLevel()];
        qualityText.text += "\nMSAA: " + QualitySettings.antiAliasing;
        qualityText.text += "\nAnisotropic: " + QualitySettings.anisotropicFiltering;
    }

    void Start()
    {
        int hresult;
        object continueevent;

        // hook keyboard to detect when the user presses a button
        _listener = new LowLevelKeyboardListener();
        _listener.OnKeyPressed += _listener_OnKeyPressed;
        _listener.HookKeyboard();

        // Setup to handle Right hand Grip actions as Recenter
        recenterAction.AddOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        // Setup to handle Left hand center click as hiding the floor
        hideFloorAction.AddOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);

        // Here at Start, let's also clip the floor to whatever the size of the user's boundary.
        // If it's not yet fully tracking, that's OK, we'll just leave as is.  This seems better
        // than adding in the SteamVR_PlayArea script.
        var chaperone = OpenVR.Chaperone;
        if (chaperone != null)
        {
            float width = 0, height = 0;
            if (chaperone.GetPlayAreaSize(ref width, ref height))
                floor.transform.localScale = new Vector3(width, height, 1);
        }

        // Here at launch, let's recenter around wherever the headset is pointing. Seems to be the 
        // model that people are expecting, instead of the facing forward based on room setup.
        RecenterHMD();


        // Store the current Texture2D on the Quad as the original grey
        screenMaterial = screen.material;
        greyTexture = screenMaterial.mainTexture;

        print("Running: " + gamePath + "\n");

        string wd = System.IO.Directory.GetCurrentDirectory();
        print("WorkingDirectory: " + wd);
        print("CurrentDirectory: " + katanga_directory);

        //print("App Directory:" + Environment.CurrentDirectory);
        //foreach (var path in Directory.GetFileSystemEntries(Environment.CurrentDirectory))
        //    print(System.IO.Path.GetFileName(path)); // file name
        //foreach (var path in Directory.GetFileSystemEntries(Environment.CurrentDirectory + "\\Assets\\Plugins\\"))
        //    print(System.IO.Path.GetFileName(path)); // file name

        _spyMgr = new NktSpyMgr();
        hresult = _spyMgr.Initialize();
        if (hresult != 0)
            throw new Exception("Deviare Initialize error.");
#if _DEBUG
        _spyMgr.SettingOverride("SpyMgrDebugLevelMask", 0x2FF8);
       // _spyMgr.SettingOverride("SpyMgrAgentLevelMask", 0x040);
#endif
        print("Successful SpyMgr Init");


        // We must set the game directory specifically, otherwise it winds up being the 
        // C# app directory which can make the game crash.  This must be done before CreateProcess.
        // This also changes the working directory, which will break Deviare's ability to find
        // the NativePlugin, so we'll use full path descriptions for the DLL load.
        // This must be reset back to the Unity game directory, otherwise Unity will
        // crash with a fatal error.

        Directory.SetCurrentDirectory(Path.GetDirectoryName(gamePath));
        {
            // Launch the game, but suspended, so we can hook our first call and be certain to catch it.

            print("Launching: " + gamePath + "...");
            _gameProcess = _spyMgr.CreateProcess(gamePath, true, out continueevent);
            if (_gameProcess == null)
                throw new Exception("CreateProcess game launch failed: " + gamePath);


            // Load the NativePlugin for the C++ side.  The NativePlugin must be in this app folder.
            // The Agent supports the use of Deviare in the CustomDLL, but does not respond to hooks.
            //
            // The native DeviarePlugin has two versions, one for x32, one for x64, so we can handle
            // either x32 or x64 games.

            _spyMgr.LoadAgent(_gameProcess);

            if (_gameProcess.PlatformBits == 64)
                _nativeDLLName = Application.dataPath + "/Plugins/DeviarePlugin64.dll";
            else
                _nativeDLLName = Application.dataPath + "/Plugins/DeviarePlugin.dll";

            int loadResult = _spyMgr.LoadCustomDll(_gameProcess, _nativeDLLName, false, true);
            if (loadResult <= 0)
            {
                int lastHR = GetLastDeviareError();
                string deadbeef = String.Format("Could not load {0}: 0x{1:X}", _nativeDLLName, lastHR);
                throw new Exception(deadbeef);
            }

            print(String.Format("Successfully loaded {0}", _nativeDLLName));


            // Hook the primary DX11 creation calls of CreateDevice, CreateDeviceAndSwapChain,
            // CreateDXGIFactory, and CreateDXGIFactory1.  These are all direct exports for either
            // D3D11.dll, or DXGI.dll. All DX11 games must call one of these interfaces to 
            // create a SwapChain.  These must be spelled exactly right, including Case.

            print("Hook the D3D11.DLL!D3D11CreateDevice...");
            NktHook deviceHook = _spyMgr.CreateHook("D3D11.DLL!D3D11CreateDevice", 0);
            if (deviceHook == null)
                throw new Exception("Failed to hook D3D11.DLL!D3D11CreateDevice");

            print("Hook the D3D11.DLL!D3D11CreateDeviceAndSwapChain...");
            NktHook deviceAndSwapChainHook = _spyMgr.CreateHook("D3D11.DLL!D3D11CreateDeviceAndSwapChain", 0);
            if (deviceAndSwapChainHook == null)
                throw new Exception("Failed to hook D3D11.DLL!D3D11CreateDeviceAndSwapChain");

            print("Hook the DXGI.DLL!CreateDXGIFactory...");
            NktHook factoryHook = _spyMgr.CreateHook("DXGI.DLL!CreateDXGIFactory", (int)eNktHookFlags.flgOnlyPostCall);
            if (factoryHook == null)
                throw new Exception("Failed to hook DXGI.DLL!CreateDXGIFactory");

            print("Hook the DXGI.DLL!CreateDXGIFactory1...");
            NktHook factory1Hook = _spyMgr.CreateHook("DXGI.DLL!CreateDXGIFactory1", (int)eNktHookFlags.flgOnlyPostCall);
            if (factory1Hook == null)
                throw new Exception("Failed to hook DXGI.DLL!CreateDXGIFactory1");


            // Hook the primary DX9 creation call of Direct3DCreate9, which is a direct export of 
            // the d3d9 DLL.  All DX9 games must call this interface, or the Direct3DCreate9Ex.
            // This is not hooked here though, it is hooked in DeviarePlugin at OnLoad.
            // We need to do special handling to fetch the System32 version of d3d9.dll,
            // in order to avoid unhooking HelixMod's d3d9.dll.

            // Hook the nvapi.  This is required to support Direct Mode in the driver, for 
            // games like Tomb Raider and Deus Ex that have no SBS.
            // There is only one call in the nvidia dll, nvapi_QueryInterface.  That will
            // be hooked, and then the _NvAPI_Stereo_SetDriverMode call will be hooked
            // so that we can see when a game sets Direct Mode and change behavior in Present.
            // This is also done in DeviarePlugin at OnLoad.


            // Make sure the CustomHandler in the NativePlugin at OnFunctionCall gets called when this 
            // object is created. At that point, the native code will take over.

            deviceHook.AddCustomHandler(_nativeDLLName, 0, "");
            deviceAndSwapChainHook.AddCustomHandler(_nativeDLLName, 0, "");
            factoryHook.AddCustomHandler(_nativeDLLName, 0, "");
            factory1Hook.AddCustomHandler(_nativeDLLName, 0, "");

            // Finally attach and activate the hook in the still suspended game process.

            deviceHook.Attach(_gameProcess, true);
            deviceHook.Hook(true);
            deviceAndSwapChainHook.Attach(_gameProcess, true);
            deviceAndSwapChainHook.Hook(true);
            factoryHook.Attach(_gameProcess, true);
            factoryHook.Hook(true);
            factory1Hook.Attach(_gameProcess, true);
            factory1Hook.Hook(true);

            // Ready to go.  Let the game startup.  When it calls Direct3DCreate9, we'll be
            // called in the NativePlugin::OnFunctionCall

            print("Continue game launch...");
            _spyMgr.ResumeProcess(_gameProcess, continueevent);
        }
        Directory.SetCurrentDirectory(katanga_directory);

        print("Restored Working Directory to: " + katanga_directory);


        // We've gotten everything launched, hooked, and setup.  Now we need to wait for the
        // game to call through to CreateDevice, so that we can create the shared surface.
    }

    
    // On Quit, we need to unhook our keyboard handler or the Editor will crash with
    // a bad handler.
    // ToDo: anything else needs to be disposed or cleaned up?

    private void OnApplicationQuit()
    {
        _listener.UnHookKeyboard();
        if (recenterAction != null)
            recenterAction.RemoveOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        if (hideFloorAction != null)
            hideFloorAction.RemoveOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);
    }


    // -----------------------------------------------------------------------------
    // Wait for the EndOfFrame, and then trigger the sync Event to allow
    // the game to continue.  Will use the _gameEventSignal from the NativePlugin
    // to trigger the Event which was actually created in the game process.
    // _gameEventSignal is a HANDLE from x32 game.
    //
    // WaitForFixedUpdate happens right at the start of next frame.
    // The goal here is to sync up the game with the VR state.  VR state needs
    // to take precedence, and is running at 90 Hz.  As long as the game can
    // maintain better than that rate, we can delay the game each frame to keep 
    // them in sync. If the game cannot keep that rate, it will drop to 1/2
    // rate at 45 Hz. Not as good, but acceptable.
    //
    // At end of frame, stall the game draw calls with TriggerEvent.

    long startTime = Stopwatch.GetTimestamp();

    // At start of frame, immediately after we've presented in VR, 
    // restart the game app.
    private IEnumerator SyncAtStartOfFrame()
    {
        int callcount = 0;
        print("SyncAtStartOfFrame, first call: " + startTime.ToString());

        while (true)
        {
            // yield, will run again after Update.  
            // This is super early in the frame, measurement shows maybe 
            // 0.5ms after start.  
            yield return null;

            callcount += 1;

            // Here at very early in frame, allow game to carry on.
            System.Int32 dummy = SetEvent;
            object deviare = dummy;
            _spyMgr.CallCustomApi(_gameProcess, _nativeDLLName, "TriggerEvent", ref deviare, false);


            long nowTime = Stopwatch.GetTimestamp();

            // print every 30 frames 
            if ((callcount % 30) == 0)
            {
                long elapsedTime = nowTime - startTime;
                double elapsedMS = elapsedTime * (1000.0 / Stopwatch.Frequency);
                print("SyncAtStartOfFrame: " + elapsedMS.ToString("F1"));
            }

            startTime = nowTime;

            // Since this is another thread as a coroutine, we won't block the main
            // drawing thread from doing its thing.
            // Wait here by CPU spin, for 9ms, close to end of frame, before we
            // pause the running game.  
            // The CPU spin here is necessary, no normal waits or sleeps on Windows
            // can do anything faster than about 16ms, which is way to slow for VR.
            // Burning one CPU core for this is not a big deal.

            double waited;
            do
            {
                waited = Stopwatch.GetTimestamp() - startTime;
                waited *= (1000.0 / Stopwatch.Frequency);
                //if ((callcount % 30) == 0)
                //{
                //    print("waiting: " + waited.ToString("F1"));
                //}
            } while (waited < 3.0);


            // Now at close to the end of each VR frame, tell game to pause.
            dummy = ResetEvent;
            deviare = dummy;
            _spyMgr.CallCustomApi(_gameProcess, _nativeDLLName, "TriggerEvent", ref deviare, false);
        }
    }

    private IEnumerator SyncAtEndofFrame()
    {
        int callcount = 0;
        long firstTime = Stopwatch.GetTimestamp();
        print("SyncAtEndofFrame, first call: " + firstTime.ToString());

        while (true)
        {
            yield return new WaitForEndOfFrame();

            // print every 30 frames 
            if ((callcount % 30) == 0)
            {
                long nowTime = Stopwatch.GetTimestamp();
                long elapsedTime = nowTime - startTime;
                double elapsedMS = elapsedTime * (1000.0 / Stopwatch.Frequency);
                print("SyncAtEndofFrame: " + elapsedMS.ToString("F1"));
            }

            //TriggerEvent(_gameEventSignal);        

            System.Int32 dummy = 0;  // ResetEvent
            object deviare = dummy;
            _spyMgr.CallCustomApi(_gameProcess, _nativeDLLName, "TriggerEvent", ref deviare, false);
        }
    }

    // -----------------------------------------------------------------------------
    //private IEnumerator UpdateFPS()
    //{
    //    TextMesh rate = GameObject.Find("rate").GetComponent<TextMesh>();

    //    while (true)
    //    {
    //        yield return new WaitForSecondsRealtime(0.2f);

    //        float gpuTime;
    //        if (XRStats.TryGetGPUTimeLastFrame(out gpuTime))
    //        {
    //            // At 90 fps, we want to know the % of a single VR frame we are using.
    //            //    gpuTime = gpuTime / ((1f / 90f) * 1000f) * 100f;
    //            rate.text = System.String.Format("{0:F1} ms", gpuTime);
    //        }
    //    }
    //}


    // -----------------------------------------------------------------------------
    // Our x64 Native DLL allows us direct access to DX11 in order to take
    // the shared handle and turn it into a ID3D11ShaderResourceView for Unity.

    [DllImport("UnityNativePlugin64")]
    private static extern IntPtr CreateSharedTexture(int sharedHandle);
    [DllImport("UnityNativePlugin64")]
    private static extern int GetGameWidth();
    [DllImport("UnityNativePlugin64")]
    private static extern int GetGameHeight();
    [DllImport("UnityNativePlugin64")]
    private static extern int GetGameFormat();

    readonly bool noMipMaps = false;
    readonly bool linearColorSpace = true;

    // PollForSharedSurface will just wait until the CreateDevice has been called in 
    // DeviarePlugin, and thus we have created a shared surface for copying game bits into.
    // This is asynchronous because it's in the game world, and we don't know when
    // it will happen.  This is a polling mechanism, which is not
    // great, but should be checking on a 4 byte HANDLE from the game side,
    // once a frame, which is every 11ms.  Only worth doing something more 
    // heroic if this proves to be a problem.
    //
    // Once the PollForSharedSurface returns with non-null, we are ready to continue
    // with the VR side of showing those bits.  This can also happen later in the
    // game if the resolution is changed mid-game, and either DX9->Reset or
    // DX11->ResizeBuffers is called.
    //
    // The goal here is to rebuild the drawing chain when the resolution changes,
    // but also to try very hard to avoid using those old textures, as they may
    // have been disposed in mid-drawing here.  This is all 100% async from the
    // game itself, as multi-threaded as it gets.  We have been getting crashes
    // during multiple resolution changes, that are likely to be related to this.

    void PollForSharedSurface()
    {
        // ToDo: To work, we need to pass in a parameter? Could use named pipe instead.
        // This will call to DeviarePlugin native DLL in the game, to fetch current gGameSurfaceShare HANDLE.
        System.Int32 native = 0; // (int)_tex.GetNativeTexturePtr();
        object parm = native;
        System.Int32 pollHandle = _spyMgr.CallCustomApi(_gameProcess, _nativeDLLName, "GetSharedHandle", ref parm, true);

        // When the game notifies us to Resize or Reset, we will set the gGameSharedHANDLE
        // to NULL to notify this side.  When this happens, immediately set the Quad
        // drawing texture to the original grey, so that we stop using the shared buffer 
        // that might very well be dead by now.

        if (pollHandle == 0)
        {
            screenMaterial.mainTexture = greyTexture;
            return;
        }

        // The game side is going to kick gGameSharedHandle to null *before* it resets the world
        // over there, so we'll likely get at least one frame of grey space.  If it's doing a full
        // screen reset, it will be much longer than that.  In any case, as soon as it switches
        // back from null to a valid Handle, we can rebuild our chain here.  This also holds
        // true for initial setup, where it will start as null.

        if (pollHandle != gGameSharedHandle)
        {
            gGameSharedHandle = pollHandle;

            print("-> Got shared handle: " + gGameSharedHandle.ToString("x"));


            // Call into the x64 UnityNativePlugin DLL for DX11 access, in order to create a ID3D11ShaderResourceView.
            // You'd expect this to be a ID3D11Texture2D, but that's not what Unity wants.
            // We also fetch the Width/Height/Format from the C++ side, as it's simpler than
            // making an interop for the GetDesc call.

            IntPtr shared = CreateSharedTexture(gGameSharedHandle);
            int width = GetGameWidth();
            int height = GetGameHeight();
            int format = GetGameFormat();

            // Really not sure how this color format works.  The DX9 values are completely different,
            // and typically the games are ARGB format there, but still look fine here once we
            // create DX11 texture with RGBA format.
            // DXGI_FORMAT_R8G8B8A8_UNORM = 28,
            // DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29,    (The Surge, DX11)
            // DXGI_FORMAT_B8G8R8A8_UNORM = 87          (The Ball, DX9)
            // DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91,
            // DXGI_FORMAT_R10G10B10A2_UNORM = 24       ME:Andromenda, Alien, CallOfCthulu   Unity RenderTextureFormat, but not TextureFormat
            //  No SRGB variant of R10G10B10A2.
            // DXGI_FORMAT_B8G8R8X8_UNORM = 88,         Trine   Unity RGB24

            // ToDo: This colorSpace doesn't do anything.
            //  Tested back to back, setting to false/true has no effect on TV gamma

            // After quite a bit of testing, this CreateExternalTexture does not appear to respect the
            // input parameters for TextureFormat, nor colorSpace.  It appears to use whatever is 
            // defined in the Shared texture as the creation parameters.  
            // If we see a format we are not presently handling properly, it's better to know about
            // it than silently do something wrong, so fire off a FatalExit.

            bool colorSpace = linearColorSpace;
            if (format == 28 || format == 87 || format == 88)
                colorSpace = linearColorSpace;
            else if (format == 29 || format == 91)
                colorSpace = !linearColorSpace;
            else if (format == 24)
                colorSpace = linearColorSpace;
            else
                MessageBox(IntPtr.Zero, String.Format("Game uses unknown DXGI_FORMAT: {0}", format), "Unknown format", 0);

            // This is the Unity Texture2D, double width texture, with right eye on the left half.
            // It will always be up to date with latest game image, because we pass in 'shared'.

            _bothEyes = Texture2D.CreateExternalTexture(width, height, TextureFormat.RGBA32, noMipMaps, colorSpace, shared);

            print("..eyes width: " + _bothEyes.width + " height: " + _bothEyes.height + " format: " + _bothEyes.format);


            // This is the primary Material for the Quad used for the virtual TV.
            // Assigning the 2x width _bothEyes texture to it means it always has valid
            // game bits.  The custom sbsShader.shader for the material takes care of 
            // showing the correct half for each eye.

            screenMaterial.mainTexture = _bothEyes;


            // These are test Quads, and will be removed.  One for each eye. Might be deactivated.
            GameObject leftScreen = GameObject.Find("left");
            if (leftScreen != null)
            {
                Material leftMat = leftScreen.GetComponent<Renderer>().material;
                leftMat.mainTexture = _bothEyes;
                // Using same primary 2x width shared texture, specify which half is used.
                leftMat.mainTextureScale = new Vector2(0.5f, 1.0f);
                leftMat.mainTextureOffset = new Vector2(0.5f, 0);
            }
            GameObject rightScreen = GameObject.Find("right");
            if (rightScreen != null)
            {
                Material rightMat = rightScreen.GetComponent<Renderer>().material;
                rightMat.mainTexture = _bothEyes;
                rightMat.mainTextureScale = new Vector2(0.5f, 1.0f);
                rightMat.mainTextureOffset = new Vector2(0.0f, 0);
            }

            // With the game fully launched and showing frames, we no longer need InfoText.
            // Setting it Inactive makes it not take any drawing cycles, as opposed to an empty string.
            infoText.gameObject.SetActive(false);
        }
    }

    // -----------------------------------------------------------------------------
    // Update is called once per frame, before rendering. Great diagram:
    // https://docs.unity3d.com/Manual/ExecutionOrder.html
    // Update is much slower than coroutines.  Unless it's required for VR, skip it.

    void Update()
    {
        // Keep checking for a change in resolution by the game. This needs to be
        // done every frame to avoid using textures disposed by Reset.
        PollForSharedSurface();

        // Doing GC on an ongoing basis is recommended for VR, to avoid weird stalls
        // at random times.
        if (Time.frameCount % 30 == 0)
            System.GC.Collect();

        if (Input.GetKey("escape"))
            Application.Quit();

        // The triangle from camera to quad edges is setup as
        //  camera: 0, 1, 0
        //  quad:   0, 2.75, 5
        // So the distance to screen is h=5, and width is w=8.
        // Triangle calculator says the inner angle and corner angle is thus
        //  1.349 rad  0.896 rad
        // h=w/2*tan(corner) => w=h*2/tan(corner) 

        double h, w;
        float width;
        if (Input.GetAxis("Mouse ScrollWheel") < 0)
        {
            this.transform.position += Vector3.back;
            h = this.transform.position.z;
            w = h * 2 / Math.Tan(0.896);
            width = (float)w;
            this.transform.localScale = new Vector3(width, -width * 9 / 16, 1);
        }
        if (Input.GetAxis("Mouse ScrollWheel") > 0)
        {
            this.transform.position += Vector3.forward;
            h = this.transform.position.z;
            w = h * 2 / Math.Tan(0.896);
            width = (float)w;
            this.transform.localScale = new Vector3(width, -width * 9 / 16, 1);
        }
    }

    // -----------------------------------------------------------------------------

    // Error handling.  Anytime we get an error that should *never* happen, we'll
    // just exit by putting up a MessageBox. We still want to check for any and all
    // possible error returns. Whenever we throw an exception anywhere in C# or
    // the C++ plugin, it will come here, so we can use the throw on fatal error model.
    //
    // We are using user32.dll MessageBox, instead of Windows Forms, because Unity
    // only supports an old version of .Net, because of its antique Mono runtime.

    [DllImport("user32.dll")]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

    static void FatalExit(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception)
        {
            MessageBox(IntPtr.Zero, condition, "Fatal Error", 0);
            Application.Quit();
        }
    }


    // Deviare has a bizarre model where they don't actually return HRESULT for calls
    // that are defined that way.  Suggestion is to use GetLastError to get the real
    // error.  This is problematic, because the DeviareCOM.dll must be found to do
    // this. So, encapsulating all that here to get and print the real error.
    //
    // Also for some damn reason the LoadCustomDLL call can also return 2, not just
    // 1, so that's extra special.  0 means it failed.  Backwards of HRESULT.
    //
    // https://github.com/nektra/Deviare2/issues/32

    [DllImport("DeviareCOM64.dll")]
    static extern int GetLastErrorCode();

    int GetLastDeviareError()
    {
        // We set back to the katanga_directory here, in case we throw
        // an error.  This keeps the editor from crashing.
        string activeDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(katanga_directory);

        int result;
        result = GetLastErrorCode();
        print(string.Format("Last Deviare error: 0x{0:X}", result));

        Directory.SetCurrentDirectory(activeDirectory);

        return result;
    }

}

//// Sierpinksky triangles for a default view, shows if other updates fail.
//for (int y = 0; y < _tex.height; y++)
//{
//    for (int x = 0; x < _tex.width; x++)
//    {
//        Color color = ((x & y) != 0 ? Color.white : Color.grey);
//        _tex.SetPixel(x, y, color);
//    }
//}
//// Call Apply() so it's actually uploaded to the GPU
//_tex.Apply();
