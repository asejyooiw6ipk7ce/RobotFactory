using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ConveyorSystem
{
    /// <summary>
    /// Handles player interactions with parcels and conveyors in the scene.
    /// Supports picking up, placing, and dropping parcels using mouse input.
    /// Left click to pick up or place parcels on conveyors, right click to drop held parcels.
    /// Works with both the Input System package and the legacy Input Manager.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        /// <summary>
        /// Camera used for raycasting player interactions (defaults to main camera if not set).
        /// </summary>
        [Tooltip("Camera used for raycasting player interactions (defaults to main camera if not set)")]
        public Camera PlayerCamera;

        /// <summary>
        /// Maximum distance for raycasting interactions in world units.
        /// </summary>
        [Tooltip("Maximum distance for raycasting interactions in world units")]
        public float MaxDistance = 5f;

        /// <summary>
        /// Transform where held parcels will be attached (defaults to this transform if not set).
        /// </summary>
        [Tooltip("Transform where held parcels will be attached (defaults to this transform if not set)")]
        public Transform HoldParent;

        /// <summary>
        /// Reference to the currently held parcel, or null if no parcel is held.
        /// </summary>
        private Parcel heldParcel;

        /// <summary>
        /// Reference to the last highlighted object to handle highlighting state changes.
        /// </summary>
        private Highlightable lastHighlight;

        /// <summary>
        /// Called once per frame to process player input and interactions.
        /// Handles raycasting, highlighting, picking up, placing, and dropping parcels.
        /// </summary>
        private void Update()
        {
            // Ensure PlayerCamera is assigned (fallback to main camera)
            if (PlayerCamera == null) PlayerCamera = Camera.main;
            if (PlayerCamera == null) return; // Safety check if no camera exists

            // Raycast from the camera to the mouse position to detect interactable objects
            if (!TryGetPointer(out Vector2 pointer, out bool leftDown, out bool rightDown)) return;

            Ray ray = PlayerCamera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out RaycastHit hit, MaxDistance))
            {
                // Handle highlighting
                var highlightable = hit.collider.GetComponentInParent<Highlightable>();
                if (lastHighlight != highlightable)
                {
                    if (lastHighlight) lastHighlight.SetHighlight(false);
                    lastHighlight = highlightable;
                    if (lastHighlight) lastHighlight.SetHighlight(true);
                }

                // Handle left click interactions
                if (leftDown)
                {
                    // If no parcel is held, try to pick one up
                    if (heldParcel == null)
                    {
                        var parcel = hit.collider.GetComponentInParent<Parcel>();
                        if (parcel != null)
                        {
                            parcel.PickUp(HoldParent ? HoldParent : transform);
                            heldParcel = parcel;
                        }
                    }
                    else
                    {
                        // If holding a parcel, try to place it on a conveyor
                        var conveyor = hit.collider.GetComponentInParent<ConveyorBase>();
                        if (conveyor != null)
                        {
                            bool placed = heldParcel.PlaceOnConveyor(conveyor);
                            if (placed) heldParcel = null;
                        }
                    }
                }
            }
            else
            {
                // Clear highlight if raycast hits nothing
                if (lastHighlight)
                {
                    lastHighlight.SetHighlight(false);
                    lastHighlight = null;
                }
            }

            // Drop held parcel with right click
            if (rightDown && heldParcel != null)
            {
                heldParcel.StartFalling();
                heldParcel = null;
            }
        }

        /// <summary>
        /// Reads the pointer position and mouse button presses for this frame from whichever input backend is active.
        /// </summary>
        /// <returns>False if no mouse is available.</returns>
        private static bool TryGetPointer(out Vector2 position, out bool leftDown, out bool rightDown)
        {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                position = Vector2.zero;
                leftDown = rightDown = false;
                return false;
            }
            position = mouse.position.ReadValue();
            leftDown = mouse.leftButton.wasPressedThisFrame;
            rightDown = mouse.rightButton.wasPressedThisFrame;
            return true;
#else
            position = Input.mousePosition;
            leftDown = Input.GetMouseButtonDown(0);
            rightDown = Input.GetMouseButtonDown(1);
            return true;
#endif
        }
    }
}