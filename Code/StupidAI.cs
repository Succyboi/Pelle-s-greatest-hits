using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(BaseAI))]
public class StupidAI : MonoBehaviour
{
    #region Variables

    [Header("Speed")]
    [Tooltip("Converts the steering magnitude to a max speed cap.")]
    public AnimationCurve sMagToSpeedCurve;
    [Tooltip("When true, the AI will attempt to drive forward at maximum speed.")]
    public bool sendIt;

    [Header("Steering")]
    [Tooltip("When true, the calculated steering direction is normalized.")]
    public bool normalizeSteeringDir;
    [Tooltip("The steering mode.")]
    public StupidSteerMode steerMode;
    [Tooltip("Dictates how steering magnitude amplifies itself.")]
    public AnimationCurve steeringCurve;
    [Tooltip("When true, the steering curve is used.")]
    public bool useCurve;
    [Tooltip("The offset added when finding the closest node.")]
    public Vector3 steerProjectionOffset;
    [Tooltip("The offset added to the closest node when deciding on the target position.")]
    public int nearestNodeOffset;
    [Tooltip("How many nodes should be sampled when calculating the target position.")]
    public int sampleSize;
    [Tooltip("How far the samples should be spread apart.")]
    public int spread;

    [Header("Non-navigational steering")]
    [Tooltip("The offset from which the push and pull raycasts should start.")]
    public Vector3 navRaycastOffset;
    [Tooltip("The push raycast directions.")]
    public Vector3[] pushRaycasts;
    [Tooltip("The layers that the push raycasts should hit.")]
    public LayerMask pushLayermask;
    [Tooltip("How much the push direction should be added to the steering direction.")]
    public float pushAmount;
    [Tooltip("When true, the push direction is normalized.")]
    public bool normalizePush;
    [Tooltip("The color of the push raycast gizmos.")]
    public Color pushColor = Color.red;

    [Tooltip("The pull raycast directions.")]
    public Vector3[] pullRaycasts;
    [Tooltip("The layers that the pull raycasts should hit.")]
    public LayerMask pullLayermask;
    [Tooltip("How much the pull direction should be added to the steering direction.")]
    public float pullAmount;
    [Tooltip("When true, the pull direction is normalized.")]
    public bool normalizePull;
    [Tooltip("The color of the pull raycast gizmos.")]
    public Color pullColor = Color.blue;

    [Header("Hazard and item avoidance")]
    [Tooltip("The point from which the avoidance box should start.")]
    public Vector3 avoidanceBoxOffset;
    [Tooltip("The size of the avoidance box.")]
    public Vector3 avoidanceBoxSize;
    [Tooltip("The layers that the avoidance box should hit.")]
    public LayerMask avoidanceBoxLayermask;
    [Tooltip("How much the avoidance direction should be added to the steering direction.")]
    public float avoidanceAmount;
    [Tooltip("When true, the avoidance direction is normalized.")]
    public bool normalizeAvoidance;
    [Tooltip("The color of the avoidance box gizmos.")]
    public Color avoidanceColor = Color.magenta;

    private Collider[] avoidanceTargets;

    [Header("Items")]
    [Tooltip("The offset from which the item raycasts should start.")]
    public Vector3 itemRaycastOffset;
    [Tooltip("The item raycast directions.")]
    public Vector3[] itemRaycasts;
    [Tooltip("The layers that the item raycasts should hit.")]
    public LayerMask itemLayermask;
    [Tooltip("The color of the item raycast gizmos.")]
    public Color itemColor = Color.green;

    [Header("Pathfinding")]
    [Tooltip("The pathfinding mode.")]
    public StupidPathfindingMode pathfindingMode;
    [Tooltip("The search mode.")]
    public StupidSearchMode searchMode;
    private int maxLoops = 10000;

    [Header("Bezier path")]
    [Tooltip("The color of the path gizmos.")]
    public Color pathColor = Color.cyan;
    [Tooltip("The range from the car where the path is visible.")]
    public float visibleRange;
    [Range(1, 16), Tooltip("Amount of times we should smooth the path.")]
    public int smoothingPasses;
    [Range(0f, 1f), Tooltip("How much of the path we should cut off.")]
    public float cutAmt;

    [Header("Cosmetic")]
    [Tooltip("The AI's body.")]
    public CarBody carBody;
    [Tooltip("The AI's name.")]
    public string carName;
    [Tooltip("The name's rainbow movement speed.")]
    public float nameHueSpeed;
    [Tooltip("The hue space inbetween letters.")]
    public float hueLetterSpace;
    [Tooltip("The size of the speed bar.")]
    public int speedBarSize;

    private float currentNameHue;
    private Vector3 lastPos;

    [Header("Misc (WARNING: EXPERIMENTAL)")]
    public bool goBackwards = false;
    [Range(0f, 181f)]
    public float backwardsAngleTreshold;

    [Header("Debug")]
    [SerializeField, Range(-1f, 1f), Tooltip("The current steering direction.")]
    private float steeringDir;
    [SerializeField, Range(0f, 1f), Tooltip("The current steering magnitude.")]
    private float steeringMag;

    [HideInInspector]
    public BaseAI baseAI;
    private Vector3[] nodes;
    private Vector3 targetPos;
    private int lastLap;
    private Transform lastCheckPoint;

    #endregion

    #region PathFinding

    //Simple A* to convert the node system to a vector3[]
    private void ReconstructPath()
    {
        Node startNode = baseAI.GetFirstNode();

        switch (pathfindingMode)
        {
            case StupidPathfindingMode.Greedy:
                {
                    //start sets
                    List<Node> openSet = new List<Node>();
                    List<Node> closedSet = new List<Node>();

                    //add start to the open set
                    openSet.Add(startNode);

                    int loops = 0;
                    while (openSet.Count > 0)
                    {
                        //sort
                        switch (searchMode)
                        {
                            case StupidSearchMode.Random:
                                openSet = openSet.OrderBy(r => Random.value).ToList();
                                break;

                            case StupidSearchMode.WorstFirst:
                            case StupidSearchMode.WorstFirstVertical:
                                openSet = openSet.OrderByDescending(r => r.cost).ToList();
                                break;

                            case StupidSearchMode.BestFirst:
                            case StupidSearchMode.BestFirstVertical:
                                openSet = openSet.OrderBy(r => r.cost).ToList();
                                break;
                        }

                        //get current node and make closed
                        Node currentNode = openSet[0];
                        openSet.Remove(currentNode);
                        closedSet.Add(currentNode);

                        //We've found the path!!! Now retrace it.
                        if (currentNode == startNode && loops > 0)
                        {
                            List<Vector3> newPath = new List<Vector3>();
                            newPath.Add(currentNode.transform.position);

                            Node currentRetraceNode = currentNode.parent;

                            while (currentRetraceNode != startNode)
                            {
                                newPath.Add(currentRetraceNode.transform.position);
                                currentRetraceNode = currentRetraceNode.parent;
                            }

                            newPath.Reverse();
                            nodes = newPath.ToArray();
                            break;
                        }

                        //scroll through all neighbouring nodes
                        for (int n = 0; n < currentNode.nextNodes.Length; n++)
                        {
                            Node neighbour = currentNode.nextNodes[n];

                            //if we've seen it, ignore it.
                            if ((closedSet.Contains(neighbour) && neighbour != startNode) || neighbour == null)
                            {
                                continue;
                            }

                            //calculate the gcost from the current node to the neighbour + the current nodes gcost
                            float newMovementCostToNeighbour = currentNode.cost + Vector3.Distance(currentNode.transform.position, neighbour.transform.position);

                            //if the open set doesn't yet contain the neighbour then use it (or if newmovement gcost is less than the neighbours gcost
                            if (newMovementCostToNeighbour < neighbour.cost || !openSet.Contains(neighbour))
                            {
                                switch (searchMode)
                                {
                                    case StupidSearchMode.WorstFirst:
                                        neighbour.cost = newMovementCostToNeighbour;
                                        break;

                                    case StupidSearchMode.WorstFirstVertical:
                                        neighbour.cost = newMovementCostToNeighbour + Vector3.Distance(new Vector3(0, currentNode.transform.position.y, 0), new Vector3(0, neighbour.transform.position.y, 0)) * 10;
                                        break;

                                    case StupidSearchMode.BestFirstVertical:
                                        neighbour.cost = newMovementCostToNeighbour - Vector3.Distance(new Vector3(0, currentNode.transform.position.y, 0), new Vector3(0, neighbour.transform.position.y, 0)) * 10;
                                        break;

                                    default:
                                        neighbour.cost = newMovementCostToNeighbour;
                                        break;
                                }

                                neighbour.parent = currentNode;

                                if (!openSet.Contains(neighbour))
                                {
                                    openSet.Add(neighbour);
                                }
                            }
                        }

                        //ensure we don't fail
                        loops++;
                        if (loops > maxLoops)
                        {
                            Debug.LogWarning("Exceeded max loops! Aborting...");
                            return;
                        }
                    }
                }
                break;
        }


        //apply smoothing
        for (int p = 0; p < smoothingPasses; p++)
        {
            nodes = Chaikin(nodes);
        }
    }

    public Vector3[] Chaikin(Vector3[] pts)
    {
        Vector3[] newPts = new Vector3[(pts.Length - 2) * 2 + 2];
        newPts[0] = pts[0];
        newPts[newPts.Length - 1] = pts[pts.Length - 1];

        int j = 1;
        for (int i = 0; i < pts.Length - 2; i++)
        {
            newPts[j] = pts[i] + (pts[i + 1] - pts[i]) * (1 - cutAmt);
            newPts[j + 1] = pts[i + 1] + (pts[i + 2] - pts[i + 1]) * cutAmt;
            j += 2;
        }
        return newPts;
    }

    #endregion

    #region Unity Functions

    private void Start()
    {
        baseAI = GetComponent<BaseAI>();
        baseAI.SetBody(carBody);
        lastPos = transform.position;

        //generate smoothed path
        ReconstructPath();
    }

    private void FixedUpdate()
    {
        int targetNode = Mathf.RoundToInt(Mathf.Repeat(
            GetNearestNode(transform.position + transform.InverseTransformDirection(steerProjectionOffset)) + nearestNodeOffset,
            nodes.Length));

        targetPos = nodes[targetNode];
        for(int s = 0; s < sampleSize; s++)
        {
            targetPos += nodes[Mathf.RoundToInt(Mathf.Repeat(targetNode + s * spread, nodes.Length))];
        }
        targetPos = new Vector3(
            targetPos.x / ((float)sampleSize + 1),
            targetPos.y / ((float)sampleSize + 1),
            targetPos.z / ((float)sampleSize + 1)
            );

        //calculate direction
        Vector3 dir = transform.InverseTransformDirection((targetPos - transform.position));

        //decide if we want to go backwards
        goBackwards = (Vector3.Angle(new Vector3(dir.x, 0, dir.z).normalized, new Vector3(Vector3.forward.x, 0, Vector3.forward.z).normalized) > backwardsAngleTreshold);

        //non-directional steering
        Vector3 pushSteer = Vector3.zero;
        foreach (Vector3 push in pushRaycasts)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + (transform.rotation * navRaycastOffset), (transform.rotation * push), out hit, push.magnitude, pushLayermask, QueryTriggerInteraction.Ignore))
            {
                pushSteer -= push * ((transform.position - push) - (transform.position - hit.point)).magnitude;
            }
        }
        dir += (normalizePush ? new Vector3(pushSteer.x, 0, 0).normalized : pushSteer) / (float)pushRaycasts.Length * pushAmount;

        Vector3 pullSteer = Vector3.zero;
        foreach (Vector3 pull in pullRaycasts)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + (transform.rotation * navRaycastOffset), (transform.rotation * pull), out hit, pull.magnitude, pullLayermask, QueryTriggerInteraction.Ignore))
            {
                pullSteer += pull * ((transform.position - pull) - (transform.position - hit.point)).magnitude;
            }
        }
        dir += (normalizePull ? new Vector3(pullSteer.x, 0, 0).normalized : pullSteer) / (float)pullRaycasts.Length * pullAmount;

        //hazard and item avoidance
        Vector3 avoidanceSteer = Vector3.zero;
        avoidanceTargets = Physics.OverlapBox(transform.position + (transform.rotation * avoidanceBoxOffset), avoidanceBoxSize, transform.rotation, avoidanceBoxLayermask);

        foreach (Collider target in avoidanceTargets)
        {
            avoidanceSteer += (transform.position - target.transform.position);
        }
        dir += (normalizeAvoidance ? new Vector3(avoidanceSteer.x, 0, 0).normalized : avoidanceSteer) * avoidanceAmount;
        Debug.DrawRay(transform.position, transform.rotation * (normalizeAvoidance ? new Vector3(avoidanceSteer.x, 0, 0).normalized : avoidanceSteer) * avoidanceAmount * 2, Color.white);

        //normalize direction
        dir = normalizeSteeringDir ? dir.normalized : new Vector3(dir.x, 0, 0).normalized;

        //steer
        float steer = dir.x;

        if (useCurve)
        {
            steer = steer * steeringCurve.Evaluate(Mathf.Abs(steer));
        }

        switch (steerMode)
        {
            case StupidSteerMode.Binary:
                steer = (steer < 0) ? -1 : 1;
                break;

            case StupidSteerMode.Half:
                steer = steer * .5f;
                break;

            case StupidSteerMode.Full:
                break;

            case StupidSteerMode.Double:
                steer = steer * 2f;
                break;

            case StupidSteerMode.Triple:
                steer = steer * 3f;
                break;

            case StupidSteerMode.Quadruple:
                steer = steer * 4f;
                break;
        }

        //calculate intended speed
        float intendedSpeed = sendIt ? 1 : sMagToSpeedCurve.Evaluate(Mathf.Clamp01(Mathf.Abs(steer)));

        //drive
        baseAI.SetDirection(new Vector2(steer, intendedSpeed));

        //aim for items
        baseAI.AimBack(!goBackwards);

        //override if backwards ballin
        if (goBackwards)
        {
            baseAI.SetDirection(new Vector2(-steer, -intendedSpeed));
            if (baseAI.GetCurrentItem() != Item.None)
            {
                baseAI.UseItem();
            }
        }

        //use item
        if (baseAI.GetCurrentItem() != Item.None)
        {
            foreach (Vector3 itemDir in pushRaycasts)
            {
                RaycastHit hit;
                if (Physics.Raycast(transform.position + (transform.rotation * -itemRaycastOffset), (transform.rotation * itemDir), out hit, itemDir.magnitude, itemLayermask, QueryTriggerInteraction.Ignore))
                {
                    baseAI.UseItem();
                }
            }
        }

        if (baseAI.checkpoint != lastCheckPoint)
        {
            lastCheckPoint = baseAI.checkpoint;
            if (baseAI.GetCurrentItem() != Item.None)
            {
                baseAI.UseItem();
            }
        }

        //regenerate path each lap
        if (baseAI.lap != lastLap)
        {
            lastLap = baseAI.lap;
            ReconstructPath();
        }

        //do some cosmetic stuff
        currentNameHue = Mathf.Repeat(currentNameHue + nameHueSpeed * Time.fixedDeltaTime, 1);

        string newName = "";
        for(int c = 0; c < carName.Length; c++)
        {
            Color col = Color.HSVToRGB(Mathf.Repeat(currentNameHue + hueLetterSpace * c, 1), 1, 1);
            newName += "<color=#" + ColorUtility.ToHtmlStringRGB(col) + ">" + carName[c] + "</color>";
        }

        newName += "\n[";
        float deltaSpeed = Mathf.Clamp01(Vector3.Distance(lastPos, transform.position) * Time.fixedDeltaTime * 100);
        for (int b = 0; b < speedBarSize; b++)
        {
            newName += (deltaSpeed > 1f / (float)speedBarSize * (float)b) ? "#" : "-";
        }
        newName += "]";

        baseAI.SetName(newName);
        lastPos = transform.position;

        //debug
        steeringDir = Mathf.Clamp(steer, -1f, 1f);
        steeringMag = Mathf.Clamp01(Mathf.Abs(steer));
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = pathColor;

            for (int n = 0; n < nodes.Length; n++)
            {
                if (Vector3.Distance(nodes[n], transform.position) < visibleRange)
                {
                    Gizmos.DrawSphere(nodes[n], 0.25f);

                    Gizmos.DrawLine(
                        nodes[n],
                        nodes[Mathf.RoundToInt(Mathf.Repeat(n + 1, nodes.Length))]
                        );
                }
            }

            Gizmos.color = new Color(1f - pathColor.r, 1f - pathColor.g, 1f - pathColor.b);

            Gizmos.DrawSphere(targetPos, 1f);
        }

        //draw push and pull steering
        Gizmos.color = pushColor;
        foreach (Vector3 push in pushRaycasts)
        {
            Gizmos.DrawRay(transform.position + navRaycastOffset, transform.rotation * push);
        }

        Gizmos.color = pullColor;
        foreach (Vector3 pull in pullRaycasts)
        {
            Gizmos.DrawRay(transform.position + navRaycastOffset, transform.rotation * pull);
        }

        //draw hazard and item avoidance
        Gizmos.color = avoidanceColor;
        Matrix4x4 originalMatrix = Gizmos.matrix;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(avoidanceBoxOffset, avoidanceBoxSize * 2);

        Gizmos.matrix = originalMatrix;
        if(avoidanceTargets != null)
        {
            foreach (Collider target in avoidanceTargets)
            {
                if (target != null)
                {
                    Gizmos.DrawCube(target.transform.position, target.bounds.size);
                }
            }
        }

        //draw items
        if (!Application.isPlaying)
        {
            Gizmos.color = itemColor;
            foreach (Vector3 itemDir in itemRaycasts)
            {
                Gizmos.DrawRay(transform.position + (transform.rotation * itemRaycastOffset), transform.rotation * itemDir);
            }
        }
        else if (baseAI.GetCurrentItem() != Item.None)
        {
            Gizmos.color = itemColor;
            foreach (Vector3 itemDir in itemRaycasts)
            {
                Gizmos.DrawRay(transform.position + (transform.rotation * itemRaycastOffset), transform.rotation * itemDir);
            }
        }
    }

    #endregion

    #region Misc

    private int GetNearestNode(Vector3 position)
    {
        int nearest = 0;
        float distance = float.MaxValue;

        for(int n = 0; n < nodes.Length; n++)
        {
            float thisDist = Vector3.Distance(position, nodes[n]);
            if (thisDist < distance)
            {
                distance = thisDist;
                nearest = n;
            }
        }

        return nearest;
    }

    #endregion
}

#region Enums

public enum StupidSteerMode
{
    Binary,
    Half,
    Full,
    Double,
    Triple,
    Quadruple
}

public enum StupidPathfindingMode
{
    Greedy
}

public enum StupidSearchMode
{
    BestFirst,
    BestFirstVertical, //prefers vertical paths (significantly)
    WorstFirst,
    WorstFirstVertical,
    Random
}

#endregion