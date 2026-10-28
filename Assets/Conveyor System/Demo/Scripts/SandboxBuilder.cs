using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Runtime level editor for the sandbox demo: place, rotate and remove conveyor modules on a grid in Play mode,
    /// drop parcels onto belts and start/stop Pausers. Connections form automatically through the conveyor triggers.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Sandbox Builder")]
    public class SandboxBuilder : MonoBehaviour
    {
        /// <summary>
        /// What a palette entry does.
        /// </summary>
        public enum ToolKind
        {
            /// <summary>Click a Pauser to stop / start it.</summary>
            Hand,
            /// <summary>Place a conveyor prefab.</summary>
            Conveyor,
            /// <summary>Drop a parcel onto a conveyor.</summary>
            Parcel
        }

        /// <summary>
        /// One palette entry.
        /// </summary>
        [Serializable]
        public class Tool
        {
            public string Name;
            public string Key;
            public ToolKind Kind = ToolKind.Conveyor;
            public GameObject Prefab;
            public Color Color = Color.white;

            [Tooltip("Placed Unloaders get an endless parcel supply.")]
            public bool IsSource;

            [Tooltip("Placed Loaders destroy and count the parcels they receive.")]
            public bool IsSink;
        }

        [Header("Tools")]
        [Tooltip("Palette entries; index 1-9 map to keys 1-9, index 10 to key 0.")]
        public Tool[] Tools;

        [Header("Grid")]
        public Vector2Int GridMin = new Vector2Int(-11, -7);
        public Vector2Int GridMax = new Vector2Int(11, 7);

        [Tooltip("Parent of all placed modules. Modules already under it are registered on start.")]
        public Transform PlacedRoot;

        [Header("Visuals")]
        public Material GhostValid;
        public Material GhostInvalid;

        [Tooltip("Optional marker that follows the hovered cell.")]
        public Transform CellCursor;

        public Color HoverColor = new Color(1f, 0.85f, 0.3f);
        public Color EraseColor = new Color(1f, 0.3f, 0.25f);

        [Header("Flow Arrows")]
        [Tooltip("Material of the arrows showing where parcels enter (input -> center).")]
        public Material ArrowInMaterial;

        [Tooltip("Material of the arrows showing where parcels leave (center -> output).")]
        public Material ArrowOutMaterial;

        [Tooltip("Also show flow arrows on placed modules (toggle with F).")]
        public bool ShowArrowsOnPlaced = true;

        [Tooltip("Height of the arrows above the ground (the belt surface is at 0.5).")]
        public float ArrowHeight = 0.56f;

        [Header("Parcels")]
        public Parcel[] ParcelPrefabs;

        [Header("Placed Module Settings")]
        public float SourceInterval = 0.8f;
        public float PauseDuration = 0.8f;

        [Header("UI")]
        public Camera ViewCamera;

        [Tooltip("One button per tool, same order as Tools.")]
        public RectTransform[] ToolButtons;

        [Tooltip("Backgrounds of the tool buttons, same order as Tools.")]
        public Image[] ToolButtonBackgrounds;

        [Tooltip("Screen areas where the mouse does not build (HUD panels).")]
        public RectTransform[] BlockingRects;

        public TMP_Text ToolText;
        public TMP_Text HintText;
        public TMP_Text StatusText;
        public TMP_Text StatsText;
        public Color ButtonNormal = new Color(0.08f, 0.1f, 0.14f, 0.9f);
        public Color ButtonSelected = new Color(0.95f, 0.5f, 0.12f, 0.95f);

        private static readonly string[] ParcelTypeNames = { "Random", "Generic", "Fragile", "Heavy", "Perishable" };
        private static readonly Vector2Int[] Neighbours = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private readonly Dictionary<Vector2Int, ConveyorBase> grid = new Dictionary<Vector2Int, ConveyorBase>();
        private readonly Dictionary<GameObject, Vector3[]> inputFlowCache = new Dictionary<GameObject, Vector3[]>();

        private int current = 1;
        private int rotation;
        private bool manualRotation;
        private Vector2Int hoverCell = new Vector2Int(int.MinValue, int.MinValue);
        private GameObject ghost;
        private int ghostTool = -1;
        private bool ghostIsValid = true;
        private Transform ghostStaging;
        private ConveyorBase highlighted;
        private int parcelType = -1;
        private int delivered;
        private float statusUntil;
        private float statsTimer;

        private void Start()
        {
            if (ViewCamera == null) ViewCamera = Camera.main;

            if (PlacedRoot != null)
            {
                foreach (var c in PlacedRoot.GetComponentsInChildren<ConveyorBase>())
                {
                    grid[CellOf(c.transform.position)] = c;
                    Hook(c);
                    AddFlowArrows(c.gameObject, c).SetActive(ShowArrowsOnPlaced);
                }
            }

            SelectTool(Mathf.Clamp(current, 0, Tools.Length - 1));
        }

        private void Update()
        {
            HandleHotkeys();

            Vector2 pointer = DemoInput.PointerPosition;
            if (IsOverUI(pointer))
            {
                if (DemoInput.LeftPressed) ClickPalette(pointer);
                HideWorldFeedback();
                UpdateStats();
                return;
            }

            if (!TryGetCell(pointer, out var cell))
            {
                HideWorldFeedback();
                UpdateStats();
                return;
            }

            if (cell != hoverCell)
            {
                hoverCell = cell;
                manualRotation = false;
            }

            var occupant = GetOccupant(cell);
            var tool = Tools[current];

            if (CellCursor != null)
            {
                CellCursor.gameObject.SetActive(true);
                CellCursor.position = new Vector3(cell.x, CellCursor.position.y, cell.y);
            }

            switch (tool.Kind)
            {
                case ToolKind.Conveyor:
                    if (!manualRotation)
                    {
                        int? auto = AutoRotation(cell, tool);
                        if (auto.HasValue) rotation = auto.Value;
                    }
                    ShowGhost(cell, occupant == null);
                    SetHighlight(occupant, EraseColor);
                    if (occupant == null && DemoInput.LeftHeld) Place(tool, cell);
                    break;

                case ToolKind.Hand:
                    SetGhostVisible(false);
                    SetHighlight(occupant, HoverColor);
                    if (DemoInput.LeftPressed && occupant is PauserConveyor pauser)
                    {
                        pauser.ToggleConveyor();
                        Status(pauser.IsActive ? "Pauser started" : "Pauser stopped");
                    }
                    break;

                case ToolKind.Parcel:
                    SetGhostVisible(false);
                    SetHighlight(occupant, HoverColor);
                    if (DemoInput.LeftPressed && occupant != null) DropParcel(occupant);
                    break;
            }

            // Right mouse removes (hold and drag to remove several)
            if (DemoInput.RightHeld && occupant != null) Remove(cell);

            UpdateStats();
        }

        // ---------- Input ----------

        private void HandleHotkeys()
        {
            for (int i = 1; i <= 9; i++)
                if (DemoInput.GetKeyDown((DemoKey)i) && i < Tools.Length) SelectTool(i);
            if (DemoInput.GetKeyDown(DemoKey.Alpha0) && Tools.Length > 10) SelectTool(10);
            if (DemoInput.GetKeyDown(DemoKey.Escape)) SelectTool(0);

            if (DemoInput.GetKeyDown(DemoKey.P))
            {
                int parcelTool = Array.FindIndex(Tools, t => t.Kind == ToolKind.Parcel);
                if (parcelTool >= 0) SelectTool(parcelTool);
            }

            if (DemoInput.GetKeyDown(DemoKey.R))
            {
                rotation = (rotation + 1) % 4;
                manualRotation = true;
            }

            if (DemoInput.GetKeyDown(DemoKey.T))
            {
                parcelType = parcelType + 1 >= ParcelPrefabs.Length ? -1 : parcelType + 1;
                RefreshToolText();
                Status($"Parcel type: {ParcelTypeNames[parcelType + 1]}");
            }

            if (DemoInput.GetKeyDown(DemoKey.C)) ClearAll();

            if (DemoInput.GetKeyDown(DemoKey.F))
            {
                ShowArrowsOnPlaced = !ShowArrowsOnPlaced;
                foreach (var conveyor in grid.Values)
                {
                    if (conveyor == null) continue;
                    var arrows = conveyor.transform.Find(FlowArrowsName);
                    if (arrows != null) arrows.gameObject.SetActive(ShowArrowsOnPlaced);
                }
                Status(ShowArrowsOnPlaced ? "Flow arrows on" : "Flow arrows off");
            }
        }

        private bool IsOverUI(Vector2 pointer)
        {
            if (BlockingRects == null) return false;
            foreach (var rt in BlockingRects)
                if (rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, pointer, null))
                    return true;
            return false;
        }

        private void ClickPalette(Vector2 pointer)
        {
            if (ToolButtons == null) return;
            for (int i = 0; i < ToolButtons.Length && i < Tools.Length; i++)
            {
                if (ToolButtons[i] != null && RectTransformUtility.RectangleContainsScreenPoint(ToolButtons[i], pointer, null))
                {
                    SelectTool(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Intersects the mouse ray with the ground plane (y = 0) and returns the grid cell, if inside the build area.
        /// </summary>
        private bool TryGetCell(Vector2 pointer, out Vector2Int cell)
        {
            cell = default;
            if (ViewCamera == null) return false;

            Ray ray = ViewCamera.ScreenPointToRay(pointer);
            if (ray.direction.y > -1e-4f) return false;

            Vector3 p = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            cell = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            return cell.x >= GridMin.x && cell.x <= GridMax.x && cell.y >= GridMin.y && cell.y <= GridMax.y;
        }

        // ---------- Tools ----------

        /// <summary>
        /// Selects a palette entry.
        /// </summary>
        public void SelectTool(int index)
        {
            if (Tools == null || Tools.Length == 0) return;
            current = Mathf.Clamp(index, 0, Tools.Length - 1);

            if (ToolButtonBackgrounds != null)
                for (int i = 0; i < ToolButtonBackgrounds.Length; i++)
                    if (ToolButtonBackgrounds[i] != null) ToolButtonBackgrounds[i].color = i == current ? ButtonSelected : ButtonNormal;

            RefreshToolText();
        }

        private void RefreshToolText()
        {
            var tool = Tools[current];
            if (ToolText != null)
            {
                ToolText.text = tool.Kind == ToolKind.Parcel
                    ? $"{tool.Name.ToUpperInvariant()}  <size=75%><color=#B9C6D8>type: {ParcelTypeNames[parcelType + 1]}</color></size>"
                    : tool.Name.ToUpperInvariant();
            }

            if (HintText != null)
            {
                switch (tool.Kind)
                {
                    case ToolKind.Conveyor:
                        HintText.text = "<b>LMB</b> place (drag to paint)   <b>R</b> rotate   <b>RMB</b> remove   <color=#5CFF8A>green arrow = parcels in</color>   <color=#FFA040>orange arrow = parcels out</color>";
                        break;
                    case ToolKind.Hand:
                        HintText.text = "<b>LMB</b> on a Pauser to stop / start it   <b>RMB</b> remove";
                        break;
                    default:
                        HintText.text = "<b>LMB</b> on a conveyor to drop a parcel   <b>T</b> change parcel type   <b>RMB</b> remove";
                        break;
                }
            }
        }

        /// <summary>
        /// Finds the rotation (in 90 degree steps) that lets the tool's prefab continue the belt that feeds this cell.
        /// </summary>
        private int? AutoRotation(Vector2Int cell, Tool tool)
        {
            foreach (var offset in Neighbours)
            {
                var neighbour = GetOccupant(cell + offset);
                if (neighbour == null || neighbour.OutputPoints == null || neighbour.CenterPoint == null) continue;

                foreach (var output in neighbour.OutputPoints)
                {
                    if (output == null) continue;
                    Vector3 flow = Flat(output.position - neighbour.CenterPoint.position);
                    Vector3 target = output.position + flow * 0.5f;
                    if (Mathf.RoundToInt(target.x) != cell.x || Mathf.RoundToInt(target.z) != cell.y) continue;

                    foreach (var localFlow in InputFlows(tool.Prefab))
                        for (int r = 0; r < 4; r++)
                            if (Vector3.Dot(Quaternion.Euler(0f, r * 90f, 0f) * localFlow, flow) > 0.9f)
                                return r;
                }
            }

            return null;
        }

        /// <summary>
        /// Flow directions (prefab space) of all inputs of a conveyor prefab.
        /// </summary>
        private Vector3[] InputFlows(GameObject prefab)
        {
            if (inputFlowCache.TryGetValue(prefab, out var flows)) return flows;

            var c = prefab.GetComponent<ConveyorBase>();
            var list = new List<Vector3>();
            if (c != null && c.CenterPoint != null)
            {
                if (c is JoinerConveyor joiner && joiner.InputPoints != null && joiner.InputPoints.Length > 0)
                {
                    foreach (var p in joiner.InputPoints)
                        if (p != null) list.Add(Flat(c.CenterPoint.localPosition - p.localPosition));
                }
                else if (c.InputPoint != null)
                {
                    list.Add(Flat(c.CenterPoint.localPosition - c.InputPoint.localPosition));
                }
            }

            flows = list.ToArray();
            inputFlowCache[prefab] = flows;
            return flows;
        }

        // ---------- Placing / removing ----------

        private void Place(Tool tool, Vector2Int cell)
        {
            if (tool.Prefab == null) return;

            var go = Instantiate(tool.Prefab, new Vector3(cell.x, 0f, cell.y), Quaternion.Euler(0f, rotation * 90f, 0f), PlacedRoot);
            go.name = $"{tool.Name} ({cell.x}, {cell.y})";
            var conveyor = go.GetComponent<ConveyorBase>();
            grid[cell] = conveyor;

            Configure(conveyor, tool);
            Hook(conveyor);
            AddFlowArrows(go, conveyor).SetActive(ShowArrowsOnPlaced);
        }

        /// <summary>
        /// Applies sandbox-friendly settings to a freshly placed module.
        /// </summary>
        private void Configure(ConveyorBase conveyor, Tool tool)
        {
            switch (conveyor)
            {
                case SmartSplitterConveyor smart:
                    // Fragile & Perishable -> output 0, Generic & Heavy -> output 1; never stall in user-built layouts
                    smart.TypeToOutput = new[] { 1, 0, 1, 0 };
                    smart.DefaultOutputIndex = 1;
                    smart.StrictRouting = false;
                    break;
                case PauserConveyor pauser:
                    pauser.PauseDuration = PauseDuration;
                    break;
            }

            if (tool.IsSource && conveyor is UnloaderConveyor unloader)
            {
                unloader.UnloadInterval = SourceInterval;
                var source = conveyor.gameObject.AddComponent<DemoParcelSource>();
                source.ParcelPrefabs = ParcelPrefabs;
            }

            if (tool.IsSink && conveyor is LoaderConveyor) conveyor.gameObject.AddComponent<DemoParcelSink>();
        }

        private void Hook(ConveyorBase conveyor)
        {
            var sink = conveyor.GetComponent<DemoParcelSink>();
            if (sink != null) sink.Delivered += (s, p) => delivered++;
        }

        private void Remove(Vector2Int cell)
        {
            if (!grid.TryGetValue(cell, out var conveyor)) return;
            grid.Remove(cell);
            if (highlighted == conveyor) highlighted = null;
            if (conveyor != null) Destroy(conveyor.gameObject);
        }

        /// <summary>
        /// Removes every placed module and every loose parcel.
        /// </summary>
        public void ClearAll()
        {
            foreach (var conveyor in grid.Values)
                if (conveyor != null) Destroy(conveyor.gameObject);
            grid.Clear();
            highlighted = null;

#if UNITY_6000_4_OR_NEWER
            var parcels = FindObjectsByType<Parcel>(FindObjectsInactive.Include);
#else
            var parcels = FindObjectsByType<Parcel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            foreach (var parcel in parcels)
                if (parcel != null && parcel.gameObject.scene.IsValid()) Destroy(parcel.gameObject);

            Status("Cleared");
        }

        private void DropParcel(ConveyorBase conveyor)
        {
            if (ParcelPrefabs == null || ParcelPrefabs.Length == 0) return;

            var prefab = parcelType < 0 ? ParcelPrefabs[UnityEngine.Random.Range(0, ParcelPrefabs.Length)] : ParcelPrefabs[parcelType];
            Vector3 at = conveyor.InputPoint != null ? conveyor.InputPoint.position : conveyor.transform.position;
            var parcel = Instantiate(prefab, at, conveyor.transform.rotation);
            parcel.name = prefab.name;

            if (!parcel.PlaceOnConveyor(conveyor))
            {
                Destroy(parcel.gameObject);
                Status("That conveyor is busy");
            }
        }

        private ConveyorBase GetOccupant(Vector2Int cell)
        {
            if (!grid.TryGetValue(cell, out var conveyor)) return null;
            if (conveyor == null)
            {
                grid.Remove(cell);
                return null;
            }
            return conveyor;
        }

        // ---------- Feedback ----------

        private void ShowGhost(Vector2Int cell, bool valid)
        {
            if (ghostTool != current || ghost == null) BuildGhost();
            if (ghost == null) return;

            SetGhostVisible(true);
            ghost.transform.SetPositionAndRotation(new Vector3(cell.x, 0f, cell.y), Quaternion.Euler(0f, rotation * 90f, 0f));

            if (valid != ghostIsValid)
            {
                ghostIsValid = valid;
                ApplyGhostMaterial(valid ? GhostValid : GhostInvalid);
            }
        }

        /// <summary>
        /// Creates a render-only copy of the current tool's prefab (no scripts, colliders or rigidbodies).
        /// </summary>
        private void BuildGhost()
        {
            if (ghost != null) Destroy(ghost);
            ghost = null;
            ghostTool = current;

            var tool = Tools[current];
            if (tool.Kind != ToolKind.Conveyor || tool.Prefab == null) return;

            if (ghostStaging == null)
            {
                // Instantiating under an inactive parent keeps the conveyor scripts from ever running
                ghostStaging = new GameObject("Ghost Staging").transform;
                ghostStaging.gameObject.SetActive(false);
            }

            var g = Instantiate(tool.Prefab, ghostStaging);

            // Direction arrows first, while the conveyor component (and its points) still exists
            var source = g.GetComponent<ConveyorBase>();
            if (source != null) AddFlowArrows(g, source);

            foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var rb in g.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var col in g.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);

            g.name = "Ghost";
            g.transform.SetParent(null, false);
            ghost = g;
            ghostIsValid = true;
            ApplyGhostMaterial(GhostValid);
        }

        private void ApplyGhostMaterial(Material material)
        {
            if (ghost == null || material == null) return;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.parent != null && r.transform.parent.name == FlowArrowsName) continue; // arrows keep their colours
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void SetGhostVisible(bool visible)
        {
            if (ghost != null && ghost.activeSelf != visible) ghost.SetActive(visible);
        }

        private void HideWorldFeedback()
        {
            SetGhostVisible(false);
            SetHighlight(null, HoverColor);
            if (CellCursor != null) CellCursor.gameObject.SetActive(false);
        }

        private void SetHighlight(ConveyorBase conveyor, Color color)
        {
            if (highlighted == conveyor) return;

            if (highlighted != null)
            {
                var old = highlighted.GetComponent<Highlightable>();
                if (old != null) old.SetHighlight(false);
            }

            highlighted = conveyor;
            if (conveyor == null) return;

            var h = conveyor.GetComponent<Highlightable>();
            if (h == null) return;
            h.HighlightColor = color;
            h.SetHighlight(true);
        }

        private void Status(string message)
        {
            if (StatusText == null) return;
            StatusText.text = message;
            statusUntil = Time.unscaledTime + 2f;
        }

        private void UpdateStats()
        {
            if (StatusText != null && statusUntil > 0f && Time.unscaledTime > statusUntil)
            {
                StatusText.text = string.Empty;
                statusUntil = 0f;
            }

            statsTimer -= Time.unscaledDeltaTime;
            if (statsTimer > 0f || StatsText == null) return;
            statsTimer = 0.25f;

            int onBelts = 0;
            foreach (var conveyor in grid.Values)
            {
                if (conveyor == null) continue;
                if (conveyor.CurrentParcel != null) onBelts++;
                if (conveyor is MultiJoinerConveyor multi) onBelts += multi.QueuedCount;
            }

            StatsText.text = $"<color=#B9C6D8>MODULES</color>  <b>{grid.Count}</b>\n<color=#B9C6D8>ON BELTS</color>  <b>{onBelts}</b>\n<color=#B9C6D8>DELIVERED</color>  <b>{delivered}</b>";
        }

        // ---------- Flow arrows ----------

        private const string FlowArrowsName = FlowArrows.RootName;

        private GameObject AddFlowArrows(GameObject target, ConveyorBase conveyor) =>
            FlowArrows.Add(target, conveyor, ArrowInMaterial, ArrowOutMaterial, ArrowHeight);

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));

        private void OnDestroy()
        {
            if (ghost != null) Destroy(ghost);
            if (ghostStaging != null) Destroy(ghostStaging.gameObject);
        }
    }
}
