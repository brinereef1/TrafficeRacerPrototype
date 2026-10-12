using System.Collections.Generic;
using UnityEngine;

// This script builds the road as the player moves forward.
// It creates new road pieces ahead of the player and recycles old ones behind.
public class RoadManager : MonoBehaviour
{
    // Faces of a road piece used for entry and exit checks.
    public enum Side { MinX, MaxX, MinZ, MaxZ }

    [System.Serializable]
    public class SegmentDefinition
    {
        public string label = "Segment";
        public GameObject prefab;

        [Tooltip("Face of the prefab the car ENTERS through. Straight: MinZ.")]
        public Side entrySide = Side.MinZ;

        [Tooltip("Face of the prefab the car LEAVES through. Straight: MaxZ.")]
        public Side exitSide = Side.MaxZ;

        [Tooltip("Corners only. Finds the correct entry/exit sides from the mesh itself and " +
                 "ignores the two Side fields above. Needs Read/Write enabled on the model.")]
        public bool autoDetectCorner;

        [Min(0f), Tooltip("Relative chance of being picked.")]
        public float weight = 1f;

        [Tooltip("Corners only. Also use this piece with entry and exit swapped, " +
                 "so one prefab gives both left and right turns.")]
        public bool bothDirections;

        [Tooltip("Only used as the very first piece (e.g. start/finish line).")]
        public bool startOnly;
    }

    [Header("Road Pieces")]
    [SerializeField] private List<SegmentDefinition> segmentDefinitions = new List<SegmentDefinition>();

    [Header("Fences (optional)")]
    [Tooltip("Placed along both sides of straight pieces.")]
    [SerializeField] private GameObject fencePrefab;
    [SerializeField] private bool placeFences = true;
    [Tooltip("Extra gap between the road edge and the fence.")]
    [SerializeField] private float fenceExtraOffset = 0f;

    [Header("Road Layout")]
    [Tooltip("Full width of the road prefabs (all of them are 15 m wide).")]
    [SerializeField, Min(1f)] private float roadWidth = 15f;

    [Tooltip("Track distance where the first piece begins. Player normally starts at 0.")]
    [SerializeField] private float trackStartDistance = -15f;

    [Tooltip("Straight pieces forced after the start piece before any corner can appear.")]
    [SerializeField, Min(0)] private int initialStraightPieces = 2;

    [Range(0f, 1f), Tooltip("Chance that the next piece is a corner (when one is allowed).")]
    [SerializeField] private float curveChance = 0.35f;

    [SerializeField, Min(0), Tooltip("Minimum straight pieces between two corners.")]
    private int minStraightsBetweenCorners = 1;

    [SerializeField, Min(10f), Tooltip("How far ahead of the player the road is generated. " +
                                       "Must be larger than the traffic spawn distance.")]
    private float lookaheadDistance = 150f;

    [SerializeField, Min(0f), Tooltip("Pieces are recycled once the player is this far past them.")]
    private float recycleDistanceBehind = 40f;

    [SerializeField, Range(1, 20), Tooltip("Tries to find a non-overlapping piece before giving up.")]
    private int maxPlacementAttempts = 8;

    [SerializeField, Min(0f), Tooltip("How much two pieces may overlap before the placement is rejected.")]
    private float overlapTolerance = 1f;

    [Header("Lanes")]
    [SerializeField, Range(1, 8)] private int laneCount = 3;

    [Tooltip("Width of the drivable asphalt, centered on the road. ~9-11 m keeps cars off the curbs.")]
    [SerializeField, Min(1f)] private float drivableWidth = 9f;

    [Header("Debug")]
    [Tooltip("Play mode only: yellow = road centerline, green = lane centers.")]
    [SerializeField] private bool drawDebugGizmos = true;

    // ------------------------------------------------------------------
    // Runtime data
    // ------------------------------------------------------------------

    private class Variant
    {
        public int defIndex;
        public Vector3 entryPoint;   // prefab-local, on the road centerline
        public Vector3 exitPoint;    // prefab-local, on the road centerline
        public float entryYaw;       // prefab-local heading when entering (degrees)
        public int turn;             // -1 left, 0 straight, +1 right
        public float length;         // centerline length
        public float radius;         // centerline radius (corners)
        public float weight;
        public bool startOnly;
        public Bounds localBounds;
    }

    private class ActiveSegment
    {
        public GameObject go;
        public Transform tf;
        public int defIndex;
        public float startDistance;
        public float length;
        public float radius;
        public int turn;
        public Vector3 entryPos;
        public float entryYaw;
        public Bounds worldBounds;
        public float EndDistance => startDistance + length;
    }

    private struct Placement
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 newEndPos;
        public float newEndYaw;
        public Bounds worldBounds;
    }

    private readonly List<Variant> variants = new List<Variant>();
    private readonly List<ActiveSegment> active = new List<ActiveSegment>();
    private readonly List<Vector3> vertexBuffer = new List<Vector3>();
    private readonly List<Vector3> meshBuffer = new List<Vector3>();
    private Stack<ActiveSegment>[] pools;
    private Variant startVariant;
    private bool hasCorners;

    private Vector3 fenceCenterOffset;
    private float fenceLength;
    private float fenceWidth;
    private bool fenceReady;

    private Vector3 endPos;
    private float endYaw;
    private float endDistance;
    private int straightsSinceCorner;
    private int piecesPlaced;
    private bool initialized;

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    // Player distance along the road.
    public float PlayerDistance { get; set; }

    // Distance to the end of the road that has already been built.
    public float GeneratedEndDistance => endDistance;

    public float LookaheadDistance => lookaheadDistance;
    public int LaneCount => laneCount;
    public float LaneWidth => drivableWidth / laneCount;
    public float DrivableWidth => drivableWidth;

    // Returns the center line offset for the selected lane.
    public float GetLaneOffset(int laneIndex)
    {
        laneIndex = Mathf.Clamp(laneIndex, 0, laneCount - 1);
        return -drivableWidth * 0.5f + LaneWidth * (laneIndex + 0.5f);
    }

    // Keeps the car inside the road width.
    public float ClampLateral(float lateral, float carHalfWidth = 0f)
    {
        float half = Mathf.Max(0f, drivableWidth * 0.5f - carHalfWidth);
        return Mathf.Clamp(lateral, -half, half);
    }

    // Finds the lane that matches the given side-to-side position.
    public int GetNearestLane(float lateral)
    {
        int lane = Mathf.FloorToInt((lateral + drivableWidth * 0.5f) / LaneWidth);
        return Mathf.Clamp(lane, 0, laneCount - 1);
    }

    // Gets the world position and rotation for a point on the road.
    public void GetPose(float distance, float lateralOffset, out Vector3 position, out Quaternion rotation)
    {
        ActiveSegment seg = FindSegment(distance);
        if (seg == null)
        {
            position = transform.position;
            rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            return;
        }

        float t = distance - seg.startDistance;
        float yaw;
        Vector3 pos;
        float entryRad = seg.entryYaw * Mathf.Deg2Rad;

        if (seg.turn == 0)
        {
            Vector3 forward = new Vector3(Mathf.Sin(entryRad), 0f, Mathf.Cos(entryRad));
            pos = seg.entryPos + forward * t;
            yaw = seg.entryYaw;
        }
        else
        {
            float sweptRad = t / seg.radius;
            Vector3 rightAtEntry = new Vector3(Mathf.Cos(entryRad), 0f, -Mathf.Sin(entryRad));
            Vector3 center = seg.entryPos + rightAtEntry * (seg.radius * seg.turn);

            yaw = seg.entryYaw + seg.turn * sweptRad * Mathf.Rad2Deg;
            float yawRad = yaw * Mathf.Deg2Rad;
            Vector3 rightNow = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
            pos = center - rightNow * (seg.radius * seg.turn);
        }

        float r = yaw * Mathf.Deg2Rad;
        Vector3 right = new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        position = pos + right * lateralOffset;
        rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // Rebuilds the road from the start so a restart can reuse this object.
    public void ResetRoad()
    {
        if (!initialized)
            return;

        for (int i = active.Count - 1; i >= 0; i--)
            ReturnSegment(active[i]);
        active.Clear();

        PlayerDistance = 0f;
        ResetPathState();
        GenerateAhead();
    }

    // ------------------------------------------------------------------
    // Unity
    // ------------------------------------------------------------------

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        if (!initialized)
            return;

        GenerateAhead();
        RecycleSegments();
    }

    private void OnValidate()
    {
        if (segmentDefinitions == null)
            return;

        foreach (SegmentDefinition def in segmentDefinitions)
        {
            if (def != null && def.entrySide == def.exitSide)
                Debug.LogWarning($"RoadManager: '{def.label}' has the same entry and exit side.", this);
        }
    }

    // ------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------

    private void Initialize()
    {
        if (segmentDefinitions == null || segmentDefinitions.Count == 0)
        {
            Debug.LogError("RoadManager: No road pieces assigned.", this);
            enabled = false;
            return;
        }

        pools = new Stack<ActiveSegment>[segmentDefinitions.Count];

        for (int i = 0; i < segmentDefinitions.Count; i++)
        {
            pools[i] = new Stack<ActiveSegment>();
            SegmentDefinition def = segmentDefinitions[i];

            if (def == null || def.prefab == null)
            {
                Debug.LogWarning($"RoadManager: Piece {i} has no prefab and is ignored.", this);
                continue;
            }

            bool isCornerSides = IsXSide(def.entrySide) != IsXSide(def.exitSide);
            bool detect = def.autoDetectCorner && !def.startOnly && isCornerSides;
            vertexBuffer.Clear();

            if (!TryMeasure(def.prefab, out Bounds bounds, detect ? vertexBuffer : null))
            {
                Debug.LogWarning($"RoadManager: '{def.label}' has no Renderer and is ignored.", this);
                continue;
            }

            Side entry = def.entrySide;
            Side exit = def.exitSide;
            float centerRadius = -1f;

            if (detect)
            {
                if (DetectCornerSides(bounds, out Side detectedEntry, out Side detectedExit,
                                      out float innerRadius, out float outerRadius))
                {
                    entry = detectedEntry;
                    exit = detectedExit;
                    centerRadius = (innerRadius + outerRadius) * 0.5f;
                    Debug.Log($"RoadManager: '{def.label}' detected as corner. Entry = {entry}, Exit = {exit}, " +
                              $"bounds = {bounds.size.x:F2} x {bounds.size.z:F2}, inner radius = {innerRadius:F2}, " +
                              $"outer radius = {outerRadius:F2}, centerline radius = {centerRadius:F2}.", this);
                }
                else
                {
                    Debug.LogWarning($"RoadManager: Could not auto-detect '{def.label}', using its Entry/Exit sides.", this);
                }
            }

            bool bothWays = def.bothDirections && !def.startOnly;
            AddVariant(i, def, bounds, entry, exit, bothWays ? def.weight * 0.5f : def.weight, centerRadius);
            if (bothWays)
                AddVariant(i, def, bounds, exit, entry, def.weight * 0.5f, centerRadius);
        }

        foreach (Variant v in variants)
        {
            if (v.startOnly && v.turn == 0 && startVariant == null)
                startVariant = v;
            else if (!v.startOnly && v.turn != 0)
                hasCorners = true;
        }

        if (PickVariant(false) == null)
        {
            Debug.LogError("RoadManager: At least one straight piece (not 'Start Only') is required.", this);
            enabled = false;
            return;
        }

        if (placeFences && fencePrefab != null && TryMeasure(fencePrefab, out Bounds fb))
        {
            fenceLength = Mathf.Max(0.1f, fb.size.z);
            fenceWidth = fb.size.x;
            fenceCenterOffset = new Vector3(fb.center.x, 0f, fb.center.z);
            fenceReady = true;
        }

        ResetPathState();
        initialized = true;
        GenerateAhead();
    }

    private bool TryMeasure(GameObject prefab, out Bounds bounds, List<Vector3> worldVertices = null)
    {
        GameObject temp = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();

        bounds = default;
        bool ok = renderers.Length > 0;
        if (ok)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
        }

        if (ok && worldVertices != null)
            CollectVertices(temp, worldVertices, prefab.name);

        Destroy(temp);
        return ok;
    }

    private void CollectVertices(GameObject root, List<Vector3> output, string label)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null)
                continue;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"RoadManager: Mesh '{mesh.name}' of '{label}' is not readable. " +
                                 "Enable Read/Write in the model's import settings to use Auto Detect Corner.", this);
                output.Clear();
                return;
            }

            mesh.GetVertices(meshBuffer);
            Matrix4x4 toWorld = filters[i].transform.localToWorldMatrix;
            for (int j = 0; j < meshBuffer.Count; j++)
                output.Add(toWorld.MultiplyPoint3x4(meshBuffer[j]));
        }
    }

    // Finds which sides of a corner piece are used for entry and exit.
    private bool DetectCornerSides(Bounds b, out Side entry, out Side exit, out float innerRadius, out float outerRadius)
    {
        entry = Side.MinZ;
        exit = Side.MinX;
        innerRadius = 0f;
        outerRadius = 0f;

        if (vertexBuffer.Count == 0)
            return false;

        float bestFarthest = float.MaxValue;
        float bestNearest = 0f;
        bool bestIsMinX = true, bestIsMinZ = true;

        for (int c = 0; c < 4; c++)
        {
            bool isMinX = (c & 1) == 0;
            bool isMinZ = (c & 2) == 0;
            float cx = isMinX ? b.min.x : b.max.x;
            float cz = isMinZ ? b.min.z : b.max.z;

            float farthest = 0f;
            float nearest = float.MaxValue;
            for (int i = 0; i < vertexBuffer.Count; i++)
            {
                float dx = vertexBuffer[i].x - cx;
                float dz = vertexBuffer[i].z - cz;
                float d = dx * dx + dz * dz;
                if (d > farthest) farthest = d;
                if (d < nearest) nearest = d;
            }

            if (farthest < bestFarthest)
            {
                bestFarthest = farthest;
                bestNearest = nearest;
                bestIsMinX = isMinX;
                bestIsMinZ = isMinZ;
            }
        }

        entry = bestIsMinZ ? Side.MinZ : Side.MaxZ;
        exit = bestIsMinX ? Side.MinX : Side.MaxX;
        innerRadius = Mathf.Sqrt(bestNearest);
        outerRadius = Mathf.Sqrt(bestFarthest);
        return true;
    }

    private void ResetPathState()
    {
        endPos = transform.position;
        endYaw = transform.eulerAngles.y;
        endDistance = trackStartDistance;
        straightsSinceCorner = 999;
        piecesPlaced = 0;
    }

    // ------------------------------------------------------------------
    // Variant construction (connection points from bounding boxes)
    // ------------------------------------------------------------------

    private void AddVariant(int defIndex, SegmentDefinition def, Bounds b, Side entry, Side exit, float weight,
                            float cornerCenterRadius)
    {
        Vector3 entryDir = InwardNormal(entry);
        Vector3 exitDir = -InwardNormal(exit);
        float entryYaw = YawOf(entryDir);
        float exitYaw = YawOf(exitDir);
        float delta = Mathf.DeltaAngle(entryYaw, exitYaw);

        int turn;
        if (Mathf.Abs(delta) < 45f) turn = 0;
        else if (Mathf.Abs(delta) < 135f) turn = delta > 0f ? 1 : -1;
        else
        {
            Debug.LogError($"RoadManager: '{def.label}' entry/exit sides turn the car around. Piece ignored.", this);
            return;
        }

        Vector3 entryPoint = ConnectionPoint(b, entry, exit, cornerCenterRadius);
        Vector3 exitPoint = ConnectionPoint(b, exit, entry, cornerCenterRadius);
        float chord = Vector3.Distance(entryPoint, exitPoint);

        float length;
        float radius = 0f;
        if (turn == 0)
        {
            length = chord;
        }
        else
        {
            radius = chord / Mathf.Sqrt(2f);
            length = Mathf.PI * 0.5f * radius;
        }

        if (length < 0.01f)
        {
            Debug.LogError($"RoadManager: '{def.label}' has zero length. Piece ignored.", this);
            return;
        }

        variants.Add(new Variant
        {
            defIndex = defIndex,
            entryPoint = entryPoint,
            exitPoint = exitPoint,
            entryYaw = entryYaw,
            turn = turn,
            length = length,
            radius = radius,
            weight = weight,
            startOnly = def.startOnly,
            localBounds = b
        });
    }

    private static bool IsXSide(Side s) => s == Side.MinX || s == Side.MaxX;

    private static Vector3 InwardNormal(Side s)
    {
        if (s == Side.MinX) return new Vector3(1f, 0f, 0f);
        if (s == Side.MaxX) return new Vector3(-1f, 0f, 0f);
        if (s == Side.MinZ) return new Vector3(0f, 0f, 1f);
        return new Vector3(0f, 0f, -1f);
    }

    private static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    private static float SideCoord(Bounds b, Side s)
    {
        if (s == Side.MinX) return b.min.x;
        if (s == Side.MaxX) return b.max.x;
        if (s == Side.MinZ) return b.min.z;
        return b.max.z;
    }

    // Finds the point where the road centerline touches the selected side of a piece.
    private Vector3 ConnectionPoint(Bounds b, Side side, Side otherSide, float cornerCenterRadius)
    {
        if (IsXSide(side) == IsXSide(otherSide))
        {
            // Opposite faces -> straight piece, centerline is the middle of the face.
            if (IsXSide(side)) return new Vector3(SideCoord(b, side), 0f, b.center.z);
            return new Vector3(b.center.x, 0f, SideCoord(b, side));
        }

        float xFace = IsXSide(side) ? SideCoord(b, side) : SideCoord(b, otherSide);
        float zFace = IsXSide(side) ? SideCoord(b, otherSide) : SideCoord(b, side);

        float outerRadius = Mathf.Min(b.size.x, b.size.z);
        float centerRadius = cornerCenterRadius > 0f ? cornerCenterRadius : outerRadius - roadWidth * 0.5f;
        float dirX = xFace <= b.center.x ? 1f : -1f;
        float dirZ = zFace <= b.center.z ? 1f : -1f;

        if (IsXSide(side))
            return new Vector3(xFace, 0f, zFace + dirZ * centerRadius);
        return new Vector3(xFace + dirX * centerRadius, 0f, zFace);
    }

    // ------------------------------------------------------------------
    // Generation
    // ------------------------------------------------------------------

    private void GenerateAhead()
    {
        float target = PlayerDistance + lookaheadDistance;
        while (endDistance < target)
            AddNextSegment();
    }

    private void RecycleSegments()
    {
        float limit = PlayerDistance - recycleDistanceBehind;
        while (active.Count > 1 && active[0].EndDistance < limit)
        {
            ReturnSegment(active[0]);
            active.RemoveAt(0);
        }
    }

    private void AddNextSegment()
    {
        Variant chosen = null;
        Placement placement = default;

        if (piecesPlaced == 0 && startVariant != null)
        {
            chosen = startVariant;
            placement = ComputePlacement(chosen);
        }
        else
        {
            bool cornerAllowed = hasCorners
                                 && piecesPlaced > initialStraightPieces
                                 && straightsSinceCorner >= minStraightsBetweenCorners;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                bool wantCorner = cornerAllowed && Random.value < curveChance;
                Variant candidate = PickVariant(wantCorner);
                if (candidate == null)
                    continue;

                Placement p = ComputePlacement(candidate);
                if (Overlaps(p.worldBounds))
                    continue;

                chosen = candidate;
                placement = p;
                break;
            }

            if (chosen == null)
            {
                // Everything we tried would overlap: fall back to a straight piece.
                chosen = PickVariant(false);
                placement = ComputePlacement(chosen);
            }
        }

        CommitSegment(chosen, placement);
    }

    private Variant PickVariant(bool wantCorner)
    {
        float total = 0f;
        for (int i = 0; i < variants.Count; i++)
        {
            Variant v = variants[i];
            if (v.startOnly || (v.turn != 0) != wantCorner)
                continue;
            total += v.weight;
        }

        if (total <= 0f)
            return null;

        float roll = Random.value * total;
        Variant last = null;
        for (int i = 0; i < variants.Count; i++)
        {
            Variant v = variants[i];
            if (v.startOnly || (v.turn != 0) != wantCorner || v.weight <= 0f)
                continue;

            last = v;
            roll -= v.weight;
            if (roll <= 0f)
                return v;
        }

        return last;
    }

    private Placement ComputePlacement(Variant v)
    {
        Quaternion rot = Quaternion.Euler(0f, endYaw - v.entryYaw, 0f);
        Vector3 pos = endPos - rot * v.entryPoint;

        Vector3 size = rot * v.localBounds.size;
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        return new Placement
        {
            position = pos,
            rotation = rot,
            newEndPos = pos + rot * v.exitPoint,
            newEndYaw = endYaw + v.turn * 90f,
            worldBounds = new Bounds(pos + rot * v.localBounds.center, size)
        };
    }

    private bool Overlaps(Bounds b)
    {
        // The last piece is the one we connect to, so it is skipped.
        for (int i = 0; i < active.Count - 1; i++)
        {
            Bounds o = active[i].worldBounds;
            float overlapX = Mathf.Min(b.max.x, o.max.x) - Mathf.Max(b.min.x, o.min.x);
            float overlapZ = Mathf.Min(b.max.z, o.max.z) - Mathf.Max(b.min.z, o.min.z);
            if (overlapX > overlapTolerance && overlapZ > overlapTolerance)
                return true;
        }
        return false;
    }

    private void CommitSegment(Variant v, Placement p)
    {
        ActiveSegment seg = GetSegment(v);
        seg.tf.SetPositionAndRotation(p.position, p.rotation);

        seg.startDistance = endDistance;
        seg.length = v.length;
        seg.radius = v.radius;
        seg.turn = v.turn;
        seg.entryPos = endPos;
        seg.entryYaw = endYaw;
        seg.worldBounds = p.worldBounds;
        active.Add(seg);

        endPos = p.newEndPos;
        endYaw = Mathf.Repeat(p.newEndYaw, 360f);
        endDistance += v.length;
        piecesPlaced++;

        if (v.turn != 0)
            straightsSinceCorner = 0;
        else
            straightsSinceCorner++;
    }

    // ------------------------------------------------------------------
    // Pooling
    // ------------------------------------------------------------------

    private ActiveSegment GetSegment(Variant v)
    {
        ActiveSegment seg;
        Stack<ActiveSegment> pool = pools[v.defIndex];

        if (pool.Count > 0)
        {
            seg = pool.Pop();
        }
        else
        {
            SegmentDefinition def = segmentDefinitions[v.defIndex];
            GameObject go = Instantiate(def.prefab, transform);
            go.name = def.label;

            if (fenceReady && v.turn == 0)
                AttachFences(go.transform, v);

            seg = new ActiveSegment { go = go, tf = go.transform, defIndex = v.defIndex };
        }

        seg.go.SetActive(true);
        return seg;
    }

    private void ReturnSegment(ActiveSegment seg)
    {
        seg.go.SetActive(false);
        pools[seg.defIndex].Push(seg);
    }

    private void AttachFences(Transform root, Variant v)
    {
        Vector3 along = v.exitPoint - v.entryPoint;
        float length = along.magnitude;
        along /= length;

        Vector3 side = new Vector3(along.z, 0f, -along.x);
        int count = Mathf.Max(1, Mathf.RoundToInt(length / fenceLength));
        float step = length / count;
        float lateral = roadWidth * 0.5f + fenceWidth * 0.5f + fenceExtraOffset;
        float baseYaw = YawOf(along);

        for (int i = 0; i < count; i++)
        {
            Vector3 center = v.entryPoint + along * (step * (i + 0.5f));

            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion rot = Quaternion.Euler(0f, baseYaw + (s > 0 ? 180f : 0f), 0f);
                Vector3 target = center + side * (lateral * s);

                GameObject fence = Instantiate(fencePrefab, root);
                fence.transform.localPosition = target - rot * fenceCenterOffset;
                fence.transform.localRotation = rot;
            }
        }
    }

    // ------------------------------------------------------------------
    // Lookup
    // ------------------------------------------------------------------

    private ActiveSegment FindSegment(float distance)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (distance >= active[i].startDistance)
                return active[i];
        }
        return active.Count > 0 ? active[0] : null;
    }

    // ------------------------------------------------------------------
    // Debug
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos || !Application.isPlaying || !initialized)
            return;

        for (int i = 0; i < active.Count; i++)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(active[i].worldBounds.center, active[i].worldBounds.size);
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(active[i].entryPos, 0.6f);

            DrawLine(active[i], 0f, Color.yellow);
            for (int lane = 0; lane < laneCount; lane++)
                DrawLine(active[i], GetLaneOffset(lane), Color.green);
        }
    }

    private void DrawLine(ActiveSegment seg, float lateral, Color color)
    {
        Gizmos.color = color;
        GetPose(seg.startDistance, lateral, out Vector3 prev, out _);

        const float step = 2f;
        for (float d = step; d < seg.length + step; d += step)
        {
            float dist = Mathf.Min(seg.startDistance + d, seg.EndDistance - 0.001f);
            GetPose(dist, lateral, out Vector3 cur, out _);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
}