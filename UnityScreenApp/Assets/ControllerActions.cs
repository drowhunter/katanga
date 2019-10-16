﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

// This class is for changing the screen distance, or screen size, or vertical location
//
// On the Right trackpad, up/down on the trackpad will change the distance away.
// On the Left trackpad, up/down on the trackpad will change the screen vertical location.
// On the Left trackpad, left/right on the trackpad will change the screen size.

public class ControllerActions : MonoBehaviour {

    public SteamVR_Action_Boolean fartherAction;
    public SteamVR_Action_Boolean nearerAction;
    public SteamVR_Action_Boolean biggerAction;
    public SteamVR_Action_Boolean smallerAction;
    public SteamVR_Action_Boolean higherAction;
    public SteamVR_Action_Boolean lowerAction;

    // This script is attached to the main Screen object, as the most logical place
    // to put all the screen sizing and location code.
    private GameObject mainScreen;

    private readonly float wait = 0.020f;  // 20 ms
    private readonly float distance = 0.010f; // 10 cm

    //-------------------------------------------------

    private void Start()
    {
        // Here at launch, let's recenter around wherever the headset is pointing. Seems to be the 
        // model that people are expecting, instead of the facing forward based on room setup.

        RecenterHMD();

        // Let's also clip the floor to whatever the size of the user's boundary.
        // If it's not yet fully tracking, that's OK, we'll just leave as is.  This seems better
        // than adding in the SteamVR_PlayArea script.

        var chaperone = OpenVR.Chaperone;
        if (chaperone != null)
        {
            float width = 0, height = 0;
            if (chaperone.GetPlayAreaSize(ref width, ref height))
                floor.transform.localScale = new Vector3(width, height, 1);
        }
    }

    //-------------------------------------------------

    // These ChangeListeners are all added during Enable and removed on Disable, rather
    // than at Start, because they will error out if the controller is not turned on.
    // These are called when the controllers are powered up, and then off, which makes
    // it a reliable place to activate.

    private void OnEnable()
    {
        mainScreen = GameObject.Find("Screen");

        fartherAction.AddOnChangeListener(OnFartherAction, SteamVR_Input_Sources.RightHand);
        nearerAction.AddOnChangeListener(OnNearerAction, SteamVR_Input_Sources.RightHand);

        biggerAction.AddOnChangeListener(OnBiggerAction, SteamVR_Input_Sources.LeftHand);
        smallerAction.AddOnChangeListener(OnSmallerAction, SteamVR_Input_Sources.LeftHand);
        higherAction.AddOnChangeListener(OnHigherAction, SteamVR_Input_Sources.LeftHand);
        lowerAction.AddOnChangeListener(OnLowerAction, SteamVR_Input_Sources.LeftHand);

        recenterAction.AddOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        hideFloorAction.AddOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);
    }

    private void OnDisable()
    {
        if (fartherAction != null)
            fartherAction.RemoveOnChangeListener(OnFartherAction, SteamVR_Input_Sources.RightHand);
        if (nearerAction != null)
            nearerAction.RemoveOnChangeListener(OnNearerAction, SteamVR_Input_Sources.RightHand);

        if (biggerAction != null)
            biggerAction.RemoveOnChangeListener(OnBiggerAction, SteamVR_Input_Sources.LeftHand);
        if (smallerAction != null)
            smallerAction.RemoveOnChangeListener(OnSmallerAction, SteamVR_Input_Sources.LeftHand);
        if (higherAction != null)
            higherAction.RemoveOnChangeListener(OnHigherAction, SteamVR_Input_Sources.LeftHand);
        if (lowerAction != null)
            lowerAction.RemoveOnChangeListener(OnLowerAction, SteamVR_Input_Sources.LeftHand);

        if (recenterAction != null)
            recenterAction.RemoveOnChangeListener(OnRecenterAction, SteamVR_Input_Sources.RightHand);
        if (hideFloorAction != null)
            hideFloorAction.RemoveOnStateDownListener(OnHideFloorAction, SteamVR_Input_Sources.LeftHand);
    }

    //-------------------------------------------------
    
    // Whenever we get clicks on Right controller trackpad, we want to loop on moving the
    // screen either in or out. Each tick of the Coroutine is worth 10cm in 3D space.Coroutine moving;
    Coroutine moving;

    // On D-pad up click, make screen farther away
    private void OnFartherAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            moving = StartCoroutine(MovingScreen(distance));
        else
            StopCoroutine(moving);
    }

    // On D-pad down click, make screen closer.
    private void OnNearerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            moving = StartCoroutine(MovingScreen(-distance));
        else
            StopCoroutine(moving);
    }

    IEnumerator MovingScreen(float delta)
    {
        while (true)
        {
            mainScreen.transform.Translate(new Vector3(0, 0, delta));

            yield return new WaitForSeconds(wait);
        }
    }

    //-------------------------------------------------
    
    // For left/right clicks on Left trackpad, we want to loop on growing or shrinking
    // the main Screen rectangle.  Each tick of the Coroutine is worth 10cm of screen height.
    Coroutine sizing;

    // For D-pad right click, grow the Screen rectangle.
    private void OnBiggerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            sizing = StartCoroutine(SizingScreen(distance));
        else
            StopCoroutine(sizing);
    }

    // For D-pad left click, shrink the Screen rectangle.
    private void OnSmallerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            sizing = StartCoroutine(SizingScreen(-distance));
        else
            StopCoroutine(sizing);
    }

    IEnumerator SizingScreen(float delta)
    {
        while (true)
        {
            // But, the screen must maintain the aspect ratio of 16:9, so for each 1m in X, we'll
            // change 9/16m in Y.  The unintuitive negative for Y is because Unity uses the OpenGL
            // layout, and Y is inverted.
            float dX = delta;
            float dY = -(delta * 9f / 16f);
            mainScreen.transform.localScale += new Vector3(dX, dY);

            yield return new WaitForSeconds(wait);
        }
    }

    //-------------------------------------------------

    // For up/down clicks on the Left trackpad, we want to move the screen higher
    // or lower.  Each tick of the Coroutine will be worth 10cm in 3D space.
    Coroutine sliding;

    // For an up click on the left trackpad, we want to move the screen up.
    private void OnHigherAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            sliding = StartCoroutine(SlidingScreen(distance));
        else
            StopCoroutine(sliding);
    }

    // For a down click on the left trackpad, we want to move the screen down.
    private void OnLowerAction(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool active)
    {
        if (active)
            sliding = StartCoroutine(SlidingScreen(-distance));
        else
            StopCoroutine(sliding);
    }

    IEnumerator SlidingScreen(float delta)
    {
        while (true)
        {
            mainScreen.transform.Translate(new Vector3(0, delta));

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
    // to avoid making players sick.  But we can move the world around the camera, by changing
    // the player position.

    public Transform player;       // Where user is looking and head position.
    public Transform vrCamera;     // Unity camera for drawing scene.  Parent is player.

    private void RecenterHMD()
    {
        print("RecenterHMD");

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

    // -----------------------------------------------------------------------------

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

}