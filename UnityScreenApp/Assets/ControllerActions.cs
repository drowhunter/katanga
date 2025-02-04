﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

// This class is for changing the screen distance, or screen size, or vertical location
//
// On the Right trackpad, up/down on the trackpad will change the distance away.
// On the Left trackpad, up/down on the trackpad will change the screen vertical location.
// On the Left trackpad, left/right on the trackpad will change the screen size.

public class ControllerActions : MonoBehaviour
{

    public SteamVR_Action_Boolean fartherAction;
    public SteamVR_Action_Boolean nearerAction;
    public SteamVR_Action_Boolean biggerAction;
    public SteamVR_Action_Boolean smallerAction;
    public SteamVR_Action_Boolean higherAction;
    public SteamVR_Action_Boolean lowerAction;
    public SteamVR_Action_Boolean curveAction;
    public SteamVR_Action_Boolean flattenAction;

    public SteamVR_Action_Boolean hideFloorAction;
    public SteamVR_Action_Boolean recenterAction;
    public SteamVR_Action_Boolean toggleSharpening;
    public SteamVR_Action_Boolean resetAll;

    // Hint panel to left of user view.
    public GameObject billboard;

    // This script is attached to the main Screen object, as the most logical place
    // to put all the screen sizing and location code.
    public GameObject stereoScreen;
    public Transform globalScreen;  // container for screen, centered around zero.
    public Transform player;        // Where user is looking and head position.
    public Transform vrCamera;      // Unity camera for drawing scene.  Parent is player.


    private readonly float wait = 0.020f;  // 20 ms
    private readonly float distance = 0.010f; // 10 cm

    private Material sbsMaterial;
    private Vector3[] vertices = null;


    //-------------------------------------------------

    // Series of prefs fetching routines, with the important second parameter
    // which is the built in default. Any time they are changed by the user
    // their new saved prefs will be returned.
    // Having these in one spot, keeps the defaults all here.

    private static float GetSizeX()
    {
        return PlayerPrefs.GetFloat("size-x", 3.556f);
    }
    private static float GetSizeY()
    {
        return PlayerPrefs.GetFloat("size-y", -2.0f);   // 16:9 ratio starting default
    }
    private static float GetScreenY()
    {
        return PlayerPrefs.GetFloat("screen-y", 1.0f);
    }
    private static float GetScreenZ()
    {
        return PlayerPrefs.GetFloat("screen-z", 2.5f);
    }
    private static float GetCurve()                     // divide by 10 for percentage
    {                                                   // bad original saved values in the field.
        return PlayerPrefs.GetFloat("curve", 4.0f);     // Default of 40% is arbitrary, looks good, and immediately visible.
    }
    private static int GetSharpening()                  // On by default
    {
        return PlayerPrefs.GetInt("sharpening", 1);
    }
    private static float GetSharpness()
    {
        return PlayerPrefs.GetFloat("sharpness", 0.0f);
    }
    private static int GetFloor()
    {
        // In DemoMode, Snow Less, normal game mode, Snow Off (for performance)
        int defaultEnvironment = Game.slideshowMode ? 1 : 2;

        // Returns default or retrieves user setting if they change it.
        return PlayerPrefs.GetInt("floor", defaultEnvironment);
    }
    private static int GetKeyHints()
    {
        return PlayerPrefs.GetInt("keyhints", 1);       // Show billboard info on left by default
    }


    private void Start()
    {
        // Here at launch, let's recenter around wherever the user last saved the state.
        // If we have no state we'll just recenter to where the headset is pointing. Seems to be the 
        // model that people are expecting, instead of the facing forward based on room setup.
        // This will rotate and translate the GlobalScreen, a container for the actual screen.

        RecenterHMD(false);

        // Restore any saved state the user has setup.  If nothing has been setup for a given
        // state, it will use the specified defaults.  
        // For the actual screen size and location, these are the local values for the screen 
        // itself, not the screen container.

        stereoScreen.transform.localPosition = new Vector3(0.0f, GetScreenY(), GetScreenZ());
        stereoScreen.transform.localScale = new Vector3(GetSizeX(), GetSizeY(), 1);

        // If the key is missing, let's set one in registry.
        if (GetSharpness() == 0.0f)
            PlayerPrefs.SetFloat("sharpness", 1.5f);

        // Save the starting vertices from StereoScreen32x18W1L1VC mesh itself,
        // so that we can reset to default, and modify on the fly.
        if (vertices == null)
            vertices = stereoScreen.GetComponent<MeshFilter>().mesh.vertices;

        UpdateCurve();
        UpdateFloor();
        UpdateSharpening();
        UpdateBillboard();


        // Let's also clip the floor to whatever the size of the user's boundary.
        // If it's not yet fully tracking, that's OK, we'll just leave as is.  This seems better
        // than adding in the SteamVR_PlayArea script.

        //var chaperone = OpenVR.Chaperone;
        //if (chaperone != null)
        //{
        //    float width = 0, height = 0;
        //    if (chaperone.GetPlayAreaSize(ref width, ref height))
        //        floor.transform.localScale = new Vector3(width, height, 1);
        //}
    }

    //-------------------------------------------------

    // These ChangeListeners are all added during Enable and removed on Disable, rather
    // than at Start, because they will error out if the controller is not turned on.
    // These are called when the controllers are powered up, and then off, which makes
    // it a reliable place to activate.

    private void OnEnable()
    {
        fartherAction.AddOnChangeListener(OnZoomAction, SteamVR_Input_Sources.RightHand);
        nearerAction.AddOnChangeListener(OnZoomAction, SteamVR_Input_Sources.RightHand);

        biggerAction.AddOnChangeListener(OnBiggerAction, SteamVR_Input_Sources.LeftHand);
        smallerAction.AddOnChangeListener(OnSmallerAction, SteamVR_Input_Sources.LeftHand);
        higherAction.AddOnChangeListener(OnHigherAction, SteamVR_Input_Sources.LeftHand);
        lowerAction.AddOnChangeListener(OnLowerAction, SteamVR_Input_Sources.LeftHand);

        curveAction.AddOnChangeListener(OnCurveAction, SteamVR_Input_Sources.RightHand);
        flattenAction.AddOnChangeListener(OnCurveAction, SteamVR_Input_Sources.RightHand);

        recenterAction.AddOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        hideFloorAction.AddOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);
        toggleSharpening.AddOnStateDownListener(OnToggleSharpeningAction, SteamVR_Input_Sources.LeftHand);

        resetAll.AddOnStateDownListener(OnResetAllAction, SteamVR_Input_Sources.Any);
    }

    private void OnDisable()
    {
        if (fartherAction != null)
            fartherAction.RemoveOnChangeListener(OnZoomAction, SteamVR_Input_Sources.RightHand);
        if (nearerAction != null)
            nearerAction.RemoveOnChangeListener(OnZoomAction, SteamVR_Input_Sources.RightHand);

        if (biggerAction != null)
            biggerAction.RemoveOnChangeListener(OnBiggerAction, SteamVR_Input_Sources.LeftHand);
        if (smallerAction != null)
            smallerAction.RemoveOnChangeListener(OnSmallerAction, SteamVR_Input_Sources.LeftHand);
        if (higherAction != null)
            higherAction.RemoveOnChangeListener(OnHigherAction, SteamVR_Input_Sources.LeftHand);
        if (lowerAction != null)
            lowerAction.RemoveOnChangeListener(OnLowerAction, SteamVR_Input_Sources.LeftHand);

        if (curveAction != null)
            curveAction.RemoveOnChangeListener(OnCurveAction, SteamVR_Input_Sources.RightHand);
        if (flattenAction != null)
            flattenAction.RemoveOnChangeListener(OnCurveAction, SteamVR_Input_Sources.RightHand);

        if (recenterAction != null)
            recenterAction.RemoveOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        if (hideFloorAction != null)
            hideFloorAction.RemoveOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);
        if (toggleSharpening != null)
            toggleSharpening.RemoveOnStateDownListener(OnToggleSharpeningAction, SteamVR_Input_Sources.LeftHand);

        if (resetAll != null)
            resetAll.RemoveOnStateDownListener(OnResetAllAction, SteamVR_Input_Sources.Any);
    }

    //-------------------------------------------------

    // Called during update loop, to watch for user input on the keyboard or on a connected
    // xbox controller.  

    void Update()
    {
        ScreenZoom();
        ScreenBiggerSmaller();
        ScreenHigherLower();
        ScreenCurve();

        Recenter();
        CycleEnvironment();
        SharpeningToggle();
        BillboardToggle();
        CheckResetAll();
    }


    //-------------------------------------------------

    // Whenever we get clicks on Right controller trackpad, we want to loop on moving the
    // screen either in or out. Each tick of the Coroutine is worth 10cm in 3D space.
    // 
    // Dpad up or joystick up on VR controllers moves it away, down moves it closer.  This
    // uses the SteamVR InputActions, and is bound through the Unity SteamVR Input menu.
    //
    // Keyboard pageup/dn and Xbox controller right stick also work using Unity InputManager.
    // These can only be used when Katanga is frontmost, so they won't interfere with the game.

    Coroutine unityMoving = null;

    private void ScreenZoom()
    {
        float movement = Input.GetAxis("ZoomScreen") + Input.GetAxis("Controller ZoomScreen");

        if ((unityMoving == null) && (movement != 0.0f))
        {
            float delta = (movement > 0) ? distance : -distance;
            unityMoving = StartCoroutine(MovingScreen(delta));
        }
        if ((unityMoving != null) && (movement == 0.0f))
        {
            StopCoroutine(unityMoving);
            unityMoving = null;
        }
    }

    Coroutine vrMoving = null;

    private void OnZoomAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        float delta = (fromAction == fartherAction) ? distance : -distance;

        if (active)
            vrMoving = StartCoroutine(MovingScreen(delta));
        else
        {
            StopCoroutine(vrMoving);
            vrMoving = null;
        }
    }

    IEnumerator MovingScreen(float delta)
    {
        while (true)
        {
            Vector3 translate = new Vector3(0, 0, delta);

            Vector3 localPosition = stereoScreen.transform.localPosition;
            localPosition += translate;

            // Cap the screen moving at a maximum to avoid flyaway problems
            if (localPosition.z > 0.5 && localPosition.z < 8.0)
            {
                stereoScreen.transform.localPosition = localPosition;

                PlayerPrefs.SetFloat("screen-z", stereoScreen.transform.localPosition.z);

                print("Screen zoom: " + stereoScreen.transform.localPosition.z);
            }

            yield return new WaitForSeconds(wait);
        }
    }

    //-------------------------------------------------

    // Grow Screen:
    //
    // For left/right clicks on Left trackpad, we want to loop on growing or shrinking
    // the main Screen rectangle.  Each tick of the Coroutine is worth 10cm of screen height.
    //
    // For keyboard left arrow is smaller, right arrow is bigger.
    // For controller, left joystick x axis  is bigger/smaller.

    Coroutine unitySizing = null;

    private void ScreenBiggerSmaller()
    {
        float movement = Input.GetAxis("SizeScreen") + Input.GetAxis("Controller SizeScreen");

        if ((unitySizing == null) && (movement != 0.0f))
        {
            float delta = (movement > 0) ? distance : -distance;
            unitySizing = StartCoroutine(SizingScreen(delta));
        }
        if ((unitySizing != null) && (movement == 0.0f))
        {
            StopCoroutine(unitySizing);
            unitySizing = null;
        }
    }

    Coroutine vrSizing;

    // For D-pad right click, grow the Screen rectangle.
    private void OnBiggerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            vrSizing = StartCoroutine(SizingScreen(distance));
        else
            StopCoroutine(vrSizing);
    }

    // For D-pad left click, shrink the Screen rectangle.
    private void OnSmallerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            vrSizing = StartCoroutine(SizingScreen(-distance));
        else
            StopCoroutine(vrSizing);
    }

    // Whenever we are saving the x/y for the screen size, we want this to be at the 16:9 aspect
    // ratio, because at launch we want to show that normal shaped screen.  
    //
    // However, when moving, we might be moving a 4:3 or 25:9 screen, and we want them to be
    // able to see that live, like normal, so the actual move operation will be based off of
    // whatever the current game is running at.
    //
    // But, the screen must maintain the aspect ratio of 16:9, so for each 1m in X, we'll
    // change 9/16m in Y.  The unintuitive negative for Y is because Unity uses the OpenGL
    // layout, and Y is inverted.

    IEnumerator SizingScreen(float delta)
    {
        while (true)
        {
            float dY = delta;
            float dX = dY * LaunchAndPlay.gameAspectRatio;

            Vector3 scale = stereoScreen.transform.localScale;
            scale += new Vector3(dX, -dY);

            // Cap the screen scaling at a maximum to avoid flyaway problems
            if (scale.x > 1.0 && scale.x < 10.0)
            {
                stereoScreen.transform.localScale = scale;

                PlayerPrefs.SetFloat("size-x", stereoScreen.transform.localScale.x);
                PlayerPrefs.SetFloat("size-y", stereoScreen.transform.localScale.y);

                print("Screen size x,y: " + stereoScreen.transform.localScale.x + "," + stereoScreen.transform.localScale.y);
            }

            yield return new WaitForSeconds(wait);
        }
    }

    //-------------------------------------------------

    // Curve Screen:
    //
    // We want to modify the Z parameters of every vertex in the screen mesh, to support curving
    // the screen upon this action. We want to specifically move only the local coordinate, not
    // global, because the global is used to rotate and translate the entire view, and we need 
    // this to work even if moved around.
    //
    // BTW, this is a better spot for this than the original idea, which was to do this work in
    // the sbsShader itself, as some examples showed.  The coordinates there are already global
    // and thus cannot be work there.
    //
    // Whenever we get left/right clicks on Right controller trackpad, we want to loop on curving the
    // screen either in or out. Each tick of the Coroutine is worth 10cm in 3D space.
    // 
    // Joystick left or dpad up on VR controllers increases curve, right flattens.  This
    // uses the SteamVR InputActions, and is bound through the Unity SteamVR Input menu.
    //
    // Keyboard home/end and Xbox controller dpad U/D also work using Unity InputManager.
    // These can only be used when Katanga is frontmost, so they won't interfere with the game.
    //
    // The saved PlayerPref of 'curve' is a percentage, ranging from 0, flat-screen, to 100, 
    // a full 180 curve.  0.05 is arbitary based on how fast it feels like it should curve.

    Coroutine unityCurving = null;
    float curveDelta = 0.05f;

    private void ScreenCurve()
    {
        float movement = Input.GetAxis("CurveScreen") + Input.GetAxis("Controller CurveScreen");

        if ((unityCurving == null) && (movement != 0.0f))
        {
            float delta = (movement > 0) ? curveDelta : -curveDelta;
            unityCurving = StartCoroutine(CurvingScreen(delta));
        }
        if ((unityCurving != null) && (movement == 0.0f))
        {
            StopCoroutine(unityCurving);
            unityCurving = null;
        }
    }

    Coroutine vrCurving = null;

    private void OnCurveAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        float delta = (fromAction == curveAction) ? curveDelta : -curveDelta;

        if (active)
            vrCurving = StartCoroutine(CurvingScreen(delta));
        else
        {
            StopCoroutine(vrCurving);
            vrCurving = null;
        }
    }

    private static double CalculateRadius(float curve)
    {
        return Math.Exp(-curve) * 200 + 4;
    }

    // When doing the auto-curve while they hold down the control, we want to stop curving when
    // it goes past making any sense.  Once it gets flat enough we don't want to drive the curve
    // coefficient negative and bend backwards.  We'll let them overcurve if they want, greater 
    // than 180, because it doesn't blow up.

    IEnumerator CurvingScreen(float delta)
    {
        while (true)
        {
            float curve = GetCurve();
            curve += delta;

            if (curve > 0.1)
            {
                PlayerPrefs.SetFloat("curve", curve);
                UpdateCurve();
            }

            yield return new WaitForSeconds(wait);
        }
    }


    // https://www.mathsisfun.com/algebra/circle-equations.html
    // 
    // We want a simple circular curve, so formula is normal circle x^2 + y^2 = r^2
    // We traverse across X, default -4 to +4, and calculate resulting Z depth.
    // y = sqrt(r^2 - x^2) In this case, y is Z, as the depth toward the screen.
    //
    // The radius will be from player to center of screen. We only change Z parameter.
    // We want to add only the delta that the curve provides, so that at x=0 it doesn't move.
    //
    // The exp conversion of coeefficent to radius is to make the manual curve setting feel more 
    // linear.  The 200 is scaling to make it flat at coefficent=0, and the 4 is half screen width,
    // which is the minimum the radius can be without clipping.
    //
    // Modify every vertice in the array to move it to match new curve.
    // The screenMesh vertices are all in non-scaled unity form though, which is why
    // we need to tranform the point to local transform first.  We need to avoid the
    // container parent GlobalScreen rotations though, as that skews the z axis.  
    // Our local transform for StereoScreen will never be rotated, so we'll just do the
    // scaling manually to get a local Z to modify.
    //
    // It doesn't really make sense to stretch the triangles, that just gives a stretched
    // curve on the edges, and it doesn't seem like anyone would want that.  To avoid this
    // stretching as the geometry changes, we'll calculate the circumference of the upcoming
    // circle, and make the X values distribute evenly across that distance.  That will keep
    // every triangle the same size as the curve deepens. Maxmimum curve is 180°, which will
    // be when radius=halfScreenWidth. Any lesser curve is only part of the curve, in a ratio
    // of radius to the maximum, which gives us our screen width mapped onto the circumference
    // in use.  
    //
    // The x = -Cos(angle)*radius is actually just the triangle equation for opposite over
    // adjacent, and the radius is the hypothenuse of the triangle. Same for z using Sin.
    // Getting the code right for this whole sequence was really challenging.

    Vector3[] activeVertices;

    private void UpdateCurve()
    {
        float viewPercent = GetCurve() / 10.0f;

        // How many subsections there are for 180 degree screen. 32 segments/triangles across
        // for the current mesh. Seems adequate for smooth curve.
        int horVerts = 33;
        int segments = 32;

        
        double viewAngle = Math.PI * viewPercent;       // What % of max
        double stepAngle = viewAngle / segments;        // each triangle width in degrees
        double startAngle = (Math.PI - viewAngle) / 2;  // offset to first vertice on left.
        double radius = stereoScreen.transform.localScale.x / viewAngle;

        activeVertices = (Vector3[])vertices.Clone();

        for (int i = 0; i < activeVertices.Length; i++)
        {
            Vector3 point = activeVertices[i];

            double angle = (i % horVerts) * stepAngle + startAngle;
            point.x *= stereoScreen.transform.localScale.x;
            point.x = -(float)(Math.Cos(angle) * radius);
            point.z = (float)(Math.Sin(angle) * radius - radius);
            point.x /= stereoScreen.transform.localScale.x;

            activeVertices[i] = point;
        }

        Mesh screenMesh = stereoScreen.GetComponent<MeshFilter>().mesh;
        screenMesh.vertices = activeVertices;
        screenMesh.RecalculateNormals();

        print("UpdateCurve state: " + viewPercent);
    }


    //-------------------------------------------------

    // For up/down clicks on the Left trackpad, we want to move the screen higher
    // or lower.  Each tick of the Coroutine will be worth 10cm in 3D space.
    //
    // For keyboard, arrow up moves screen up, arrow down moves down.
    // For xbox controller, left stick Y axis moves screen up/down.

    Coroutine unitySliding = null;

    private void ScreenHigherLower()
    {
        float movement = Input.GetAxis("SlideScreen") + Input.GetAxis("Controller SlideScreen");

        if ((unitySliding == null) && (movement != 0.0f))
        {
            float delta = (movement > 0) ? distance : -distance;
            unitySliding = StartCoroutine(SlidingScreen(delta));
        }
        if ((unitySliding != null) && (movement == 0.0f))
        {
            StopCoroutine(unitySliding);
            unitySliding = null;
        }
    }

    Coroutine vrSliding;

    // For an up click on the left trackpad, we want to move the screen up.
    private void OnHigherAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            vrSliding = StartCoroutine(SlidingScreen(distance));
        else
            StopCoroutine(vrSliding);
    }

    // For a down click on the left trackpad, we want to move the screen down.
    private void OnLowerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            vrSliding = StartCoroutine(SlidingScreen(-distance));
        else
            StopCoroutine(vrSliding);
    }

    IEnumerator SlidingScreen(float delta)
    {
        while (true)
        {
            Vector3 location = stereoScreen.transform.localPosition;
            location.y += delta;

            // Cap the screen translation at a maximum of 3.5 to avoid flyaway problems
            if (location.y > -3.5 && location.y < 3.5)
            {
                stereoScreen.transform.localPosition = location;

                PlayerPrefs.SetFloat("screen-y", stereoScreen.transform.localPosition.y);

                print("Screen height: " + stereoScreen.transform.localPosition.y);
            }

            yield return new WaitForSeconds(wait);
        }
    }

    // -----------------------------------------------------------------------------

    // We need to allow Recenter, even for room-scale, because people ask for it. 
    // The usual Recenter does not work for room-scale because the assumption is that
    // you will simply rotate to see.  This following code sequence works in all cases.
    // https://forum.unity.com/threads/openvr-how-to-reset-camera-properly.417509/#post-2792972
    //
    // vrCamera object cannot be moved or altered, Unity VR doesn't allow moving the camera
    // to avoid making players sick.  We now move the GlobalScreen for Recenter operations,
    // as it is centered around zero, and thus only requires matching the vrCamera, which
    // will rotate and translate the contained screen.
    //
    // We want to save the user state, so they don't have to move the screen at every launch.
    // There are three states.  1) No saved state, 2) Saved state, power up, 3) Recenter command.
    // State is only saved on user command for recenter, grip on right controller.  Once saved,
    // we'll use that state for all future power up/launches.  Will be reset whenever the user
    // does another recenter command with right controller.

    private void RecenterHMD(bool saveState)
    {
        print("RecenterHMD");

        // Get current head rotation in scene (y-only, to avoid tilting the floor)

        float offsetAngle = vrCamera.localRotation.eulerAngles.y;

        if (saveState)
            PlayerPrefs.SetFloat("global-rotation", offsetAngle);
        else
            offsetAngle = PlayerPrefs.GetFloat("global-rotation", offsetAngle);

        // Now set the globalScreen euler angle to that same rotation, so that the screen
        // environment will rotate from the starting zero location to current gaze.
        // Because this is called for any Recenter operation, we don't want to simply
        // rotate from where we are, we want to set wherever the vrCamera is looking.

        Vector3 screenAngles = globalScreen.eulerAngles;
        screenAngles.y = offsetAngle;
        globalScreen.eulerAngles = screenAngles;


        // Do the same job for translation, and move the globalScreen to the same
        // offset that the vrCamer has.  Since this moves the global container instead
        // of the screen, the relative offsets and screen size will appear unchanged,
        // just centered to the viewing position.

        Vector3 offsetPos = vrCamera.position;

        if (saveState)
        {
            PlayerPrefs.SetFloat("global-x", offsetPos.x);
            PlayerPrefs.SetFloat("global-z", offsetPos.z);
        }
        else
        {
            offsetPos.x = PlayerPrefs.GetFloat("global-x", offsetPos.x);
            offsetPos.z = PlayerPrefs.GetFloat("global-z", offsetPos.z);
        }

        Vector3 globalPos = globalScreen.position;
        globalPos.x = offsetPos.x;
        globalPos.z = offsetPos.z;
        globalScreen.position = globalPos;
    }


    // We'll also handle the Right Controller Grip action as a RecenterHMD command.
    // And whenever the user is going out of there way to specify this, save that angle.

    private void OnRecenterAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            RecenterHMD(true);
    }

    // For Keyboard, recenter key is Home.
    // For xbox controller, recenter is right bumper.

    private void Recenter()
    {
        if (Input.GetButtonDown("Recenter"))
            RecenterHMD(true);
    }

    // -----------------------------------------------------------------------------

    // Upon any state change, save it as the new user preference.

    public GameObject floor;
    public Camera sky;          // As VRCamera object
    public GameObject leftEmitter;
    public GameObject rightEmitter;

    private void OnHideFloorAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource)
    {
        int state = GetFloor();
        state++;
        if (state > 4)
            state = 0;
        PlayerPrefs.SetInt("floor", state);

        UpdateFloor();
    }

    // For Keyboard, cycle environment key is backspace.
    // For xbox controller, cycle is left bumper.

    private void CycleEnvironment()
    {
        if (Input.GetButtonDown("Cycle Environment"))
        {
            int state = GetFloor();
            state++;
            if (state > 4)
                state = 0;
            PlayerPrefs.SetInt("floor", state);

            UpdateFloor();
        }
    }


    // Hide the floor on grip of left control. Now cycle:
    //  1) Snow less
    //  2) Snow off
    //  3) Sky off
    //  4) Floor off
    //  5) All on

    private void UpdateFloor()
    {
        int state = GetFloor();
        print("Set environment state: " + state);

        ParticleSystem.EmissionModule leftSnow = leftEmitter.GetComponent<ParticleSystem>().emission;
        ParticleSystem.EmissionModule rightSnow = rightEmitter.GetComponent<ParticleSystem>().emission;

        switch (state)
        {
            case 1:
                floor.SetActive(true);
                sky.clearFlags = CameraClearFlags.Skybox;
                leftEmitter.SetActive(false);                   // Disable, then enable makes immediate 
                rightEmitter.SetActive(false);                  // visible difference in rate from prewarm.
                leftSnow.rateOverTime = 50f;
                rightSnow.rateOverTime = 50f;
                leftEmitter.SetActive(true);
                rightEmitter.SetActive(true);
                break;
            case 2:
                floor.SetActive(true);
                sky.clearFlags = CameraClearFlags.Skybox;
                leftEmitter.SetActive(false);
                rightEmitter.SetActive(false);
                break;
            case 3:
                floor.SetActive(true);
                sky.clearFlags = CameraClearFlags.SolidColor;
                leftEmitter.SetActive(false);
                rightEmitter.SetActive(false);
                break;
            case 4:
                floor.SetActive(false);
                sky.clearFlags = CameraClearFlags.SolidColor;
                leftEmitter.SetActive(false);
                rightEmitter.SetActive(false);
                break;

            default:
                floor.SetActive(true);
                sky.clearFlags = CameraClearFlags.Skybox;
                leftSnow.rateOverTime = 500f;
                rightSnow.rateOverTime = 500f;
                leftEmitter.SetActive(true);
                rightEmitter.SetActive(true);
                break;
        }
    }


    // -----------------------------------------------------------------------------

    // Sharpening effect will be on by default, but the user has the option to disable it,
    // because it will cost some performance, and they may not care for it.

    private void OnToggleSharpeningAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource)
    {
        int state = GetSharpening();
        state++;
        if (state > 1)
            state = 0;
        PlayerPrefs.SetInt("sharpening", state);

        UpdateSharpening();
    }

    // For Keyboard, sharpening key is Insert.
    // For xbox controller, sharpening toggle is Y button.

    private void SharpeningToggle()
    {
        if (Input.GetButtonDown("Sharpening Toggle"))
        {
            int state = GetSharpening();
            state++;
            if (state > 1)
                state = 0;
            PlayerPrefs.SetInt("sharpening", state);

            UpdateSharpening();
        }
    }

    private void UpdateSharpening()
    {
        PrismSharpen sharpener = vrCamera.GetComponent<PrismSharpen>();

        int state = GetSharpening();
        if (state == 1)
            sharpener.enabled = true;
        else
            sharpener.enabled = false;

        float sharpness = GetSharpness();
        if (sharpness != 0.0f)
            sharpener.sharpenAmount = sharpness;

        print("Sharpening state: " + state + " sharpness: " + sharpness);
    }

    // -----------------------------------------------------------------------------

    // For Keyboard, hint key is F1.
    // For xbox controller, show/hide hint is A button.
    // This is done separately, because the VR controller may never be turned on.
    // 
    // Stored in separate PlayerPref, because if it was turned off in VR controller,
    // it may never be seen here, and vice versa.  The billboard is out of view on the
    // left side, so won't interfere while gaming.

    private void BillboardToggle()
    {
        if (Input.GetButtonDown("Hide Info"))
        {
            int state = GetKeyHints();
            state++;
            if (state > 1)
                state = 0;
            PlayerPrefs.SetInt("keyhints", state);

            UpdateBillboard();
        }
    }

    // Rather than hide this, and thus make it hard to find again, let's just rotate
    // it all the way behind the user.  This rotates a container surrounding the billboard.

    private void UpdateBillboard()
    {
        int state = GetKeyHints();
        Vector3 angles = billboard.transform.eulerAngles;

        if (state == 1)
            angles.y = -10;     // On the left
        else
            angles.y = -90;     // directly behind

        billboard.transform.eulerAngles = angles;
        
        print("Hint state: " + state);
    }


    // -----------------------------------------------------------------------------

    // Sometimes the screen can fly wildly off screen, and we need a way to clear all the saved
    // PlayerPrefs Defaults.  Has been requested on forum.

    private void OnResetAllAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource)
    {
        PlayerPrefs.DeleteAll();
        print("*** Deleted all prefs, back to defaults.");

        // Reset the environment like at launch.
        Start();
    }

    private void CheckResetAll()
    {
        if (Input.GetButton("Recenter") && Input.GetButton("Cycle Environment"))
        {
            PlayerPrefs.DeleteAll();
            print("*** Deleted all prefs, back to defaults.");

            // Reset the environment like at launch.
            Start();

            // Small delay to avoid hammering this routine for multiple frames.
            Thread.Sleep(100);
        }
    }

}
