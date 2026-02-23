using UnityEngine;

public class Waypoints : MonoBehaviour
{
    public static Transform[][] AllPaths;

    void Awake()
    {
        int pathCount = transform.childCount;
        AllPaths = new Transform[pathCount][];

        for (int i = 0; i < pathCount; i++)
        {
            Transform path = transform.GetChild(i);
            int waypointCount = path.childCount;

            AllPaths[i] = new Transform[waypointCount];
            for (int j = 0; j < waypointCount; j++)
            {
                AllPaths[i][j] = path.GetChild(j);
            }
        }
    }
}
