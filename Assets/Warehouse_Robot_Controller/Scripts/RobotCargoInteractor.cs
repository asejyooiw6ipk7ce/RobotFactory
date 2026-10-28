using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class RobotCargoInteractor : MonoBehaviour
    {
        [Header("References")]
        public GridRobotController gridController;
        public Animator animator;

        [Tooltip("Position reference for the carried box. Its rotation is ignored by this script.")]
        public Transform cargoAttachPoint;

        [Header("Stable Carry Root")]
        [Tooltip("Recommended. Creates a non-animated cargo root under this robot. The attach point is used only as a position reference.")]
        public bool createStableCarryRoot = true;

        [Tooltip("Name of the runtime stable carry root created under the robot.")]
        public string stableCarryRootName = "StableCargoRoot";

        [Header("Carry Pose")]
        [Tooltip("Local position offset relative to the stable carry root. Rotation is intentionally not changed when attaching a box.")]
        public Vector3 carriedBoxLocalPosition = Vector3.zero;

        [Header("Animator Bool Parameters")]
        public string hasBoxParameter = "HasBox";

        [Header("Animator Triggers - Load")]
        public string loadLevel0Trigger = "Load_Level_0";
        public string loadLevel1Trigger = "Load_Level_1";
        public string loadLevel2Trigger = "Load_Level_2";

        [Header("Animator Triggers - Unload")]
        public string unloadLevel0Trigger = "Unload_Level_0";
        public string unloadLevel1Trigger = "Unload_Level_1";
        public string unloadLevel2Trigger = "Unload_Level_2";

        [Header("Input")]
        public KeyCode interactKey = KeyCode.E;

        [Header("Debug")]
        public bool showDebugLogs = true;

        private ShelfInteractionZone currentZone;
        private Transform activeCarryRoot;

        private CargoBox carriedBox;
        private CargoBox pendingBox;
        private ShelfStorage pendingShelf;

        private int pendingColumn = -1;
        private int pendingLevel = -1;

        private bool isBusy;

        public bool HasBox => carriedBox != null;
        public bool IsBusy => isBusy;

        private void Awake()
        {
            SetupCarryRoot();
        }

        private void Start()
        {
            UpdateAnimatorHasBox();
        }

        private void Update()
        {
            if (isBusy)
                return;

            if (Input.GetKeyDown(interactKey))
                TryInteract();
        }

        private void SetupCarryRoot()
        {
            if (!createStableCarryRoot)
            {
                activeCarryRoot = cargoAttachPoint;
                return;
            }

            GameObject rootObject = new GameObject(string.IsNullOrEmpty(stableCarryRootName) ? "StableCargoRoot" : stableCarryRootName);
            Transform root = rootObject.transform;
            root.SetParent(transform, false);

            if (cargoAttachPoint != null)
                root.localPosition = transform.InverseTransformPoint(cargoAttachPoint.position);
            else
                root.localPosition = Vector3.zero;

            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            activeCarryRoot = root;
        }

        private void TryInteract()
        {
            if (currentZone == null)
            {
                Log("No shelf interaction zone.");
                return;
            }

            if (currentZone.shelf == null)
            {
                Log("Shelf zone has no ShelfStorage assigned.");
                return;
            }

            if (!currentZone.IsRobotCorrectlyDocked(transform))
            {
                Log("Robot is not correctly docked.");
                return;
            }

            if (carriedBox == null)
                TryLoadBox();
            else
                TryUnloadBox();
        }

        private void TryLoadBox()
        {
            ShelfStorage shelf = currentZone.shelf;
            int column = currentZone.columnIndex;

            if (!shelf.HasBoxToTake(column, out int level, out CargoBox box))
            {
                Log("No box to take in column: " + column);
                return;
            }

            pendingShelf = shelf;
            pendingColumn = column;
            pendingLevel = level;
            pendingBox = box;

            isBusy = true;
            LockRobot(true);

            string trigger = GetLoadTrigger(level);

            Log("Loading box from level " + level + " using trigger: " + trigger);

            if (animator != null)
                animator.SetTrigger(trigger);
            else
                Debug.LogWarning("RobotCargoInteractor: Animator is not assigned.");
        }

        private void TryUnloadBox()
        {
            ShelfStorage shelf = currentZone.shelf;
            int column = currentZone.columnIndex;

            if (!shelf.HasFreePlaceToPut(column, out int level))
            {
                Log("No free place to put box in column: " + column);
                return;
            }

            pendingShelf = shelf;
            pendingColumn = column;
            pendingLevel = level;
            pendingBox = carriedBox;

            isBusy = true;
            LockRobot(true);

            string trigger = GetUnloadTrigger(level);

            Log("Unloading box to level " + level + " using trigger: " + trigger);

            if (animator != null)
                animator.SetTrigger(trigger);
            else
                Debug.LogWarning("RobotCargoInteractor: Animator is not assigned.");
        }

        public void AttachBoxToRobot()
        {
            if (pendingBox == null || pendingShelf == null)
            {
                Log("AttachBoxToRobot failed: pending data is missing.");
                return;
            }

            if (activeCarryRoot == null)
            {
                Debug.LogWarning("RobotCargoInteractor: Carry root is not assigned.");
                return;
            }

            Quaternion boxWorldRotation = pendingBox.transform.rotation;
            Vector3 carryWorldPosition = GetCarryWorldPosition();

            pendingShelf.RemoveBox(pendingColumn, pendingLevel);

            carriedBox = pendingBox;
            carriedBox.isCarried = true;

            carriedBox.transform.SetParent(activeCarryRoot, true);
            carriedBox.transform.position = carryWorldPosition;
            carriedBox.transform.rotation = boxWorldRotation;

            UpdateAnimatorHasBox();

            Log("Box attached to robot without changing rotation.");
        }

        public void DetachBoxToShelf()
        {
            if (carriedBox == null || pendingShelf == null)
            {
                Log("DetachBoxToShelf failed: pending data is missing.");
                return;
            }

            Quaternion boxWorldRotation = carriedBox.transform.rotation;

            pendingShelf.PutBox(
                pendingColumn,
                pendingLevel,
                carriedBox,
                boxWorldRotation
            );

            carriedBox = null;

            UpdateAnimatorHasBox();

            Log("Box detached to shelf without changing rotation.");
        }

        public void OnCargoAnimationFinished()
        {
            pendingBox = null;
            pendingShelf = null;
            pendingColumn = -1;
            pendingLevel = -1;

            isBusy = false;
            LockRobot(false);

            UpdateAnimatorHasBox();

            Log("Cargo animation finished.");
        }

        public void ForceBoxToCarryPose()
        {
            if (carriedBox == null || activeCarryRoot == null)
                return;

            Quaternion boxWorldRotation = carriedBox.transform.rotation;

            carriedBox.transform.SetParent(activeCarryRoot, true);
            carriedBox.transform.position = GetCarryWorldPosition();
            carriedBox.transform.rotation = boxWorldRotation;
        }

        private Vector3 GetCarryWorldPosition()
        {
            if (activeCarryRoot != null)
                return activeCarryRoot.TransformPoint(carriedBoxLocalPosition);

            if (cargoAttachPoint != null)
                return cargoAttachPoint.TransformPoint(carriedBoxLocalPosition);

            return transform.TransformPoint(carriedBoxLocalPosition);
        }

        private void UpdateAnimatorHasBox()
        {
            if (animator == null)
                return;

            animator.SetBool(hasBoxParameter, carriedBox != null);
        }

        private void LockRobot(bool value)
        {
            if (gridController != null)
                gridController.controlsLocked = value;
        }

        private string GetLoadTrigger(int level)
        {
            switch (level)
            {
                case 0: return loadLevel0Trigger;
                case 1: return loadLevel1Trigger;
                case 2: return loadLevel2Trigger;
                default: return loadLevel0Trigger;
            }
        }

        private string GetUnloadTrigger(int level)
        {
            switch (level)
            {
                case 0: return unloadLevel0Trigger;
                case 1: return unloadLevel1Trigger;
                case 2: return unloadLevel2Trigger;
                default: return unloadLevel0Trigger;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            ShelfInteractionZone zone = other.GetComponent<ShelfInteractionZone>();

            if (zone == null)
                return;

            currentZone = zone;

            Log("Entered shelf zone. Column: " + zone.columnIndex + ", Side: " + zone.accessSide);
        }

        private void OnTriggerExit(Collider other)
        {
            ShelfInteractionZone zone = other.GetComponent<ShelfInteractionZone>();

            if (zone == null)
                return;

            if (zone == currentZone)
            {
                currentZone = null;
                Log("Exited shelf zone.");
            }
        }

        private void Log(string message)
        {
            if (!showDebugLogs)
                return;

            Debug.Log("RobotCargoInteractor: " + message);
        }
    }
}
