using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-time helper: puts a working MeshCollider on every mesh under this object (houses, posts, etc.).
/// Add it to the "Map" object, then click the three dots (or right-click) on the component header
/// and choose "Add Mesh Colliders To Children". You can delete the component afterwards.
/// </summary>
public class AddCollidersToChildren : MonoBehaviour
{
    [Tooltip("Objects whose name contains any of these words are skipped (thin things you don't want to crash into).")]
    public string[] skipNameContains = { "cable", "wire" };

    [ContextMenu("Add Mesh Colliders To Children")]
    void AddColliders()
    {
        int added = 0, fixedEmpty = 0, removed = 0;

        // 1) Repair Mesh Colliders that were added with no mesh (happens when added on an empty parent)
        foreach (var mc in GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.sharedMesh != null) continue;

            var mf = mc.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Record(mc);
                mc.sharedMesh = mf.sharedMesh;
                fixedEmpty++;
            }
            else
            {
                Remove(mc);
                removed++;
            }
        }

        // 2) Add a MeshCollider to every mesh that doesn't have any collider yet
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            if (ShouldSkip(mf.gameObject.name)) continue;

            var mc = Add(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
            added++;
        }

        Debug.Log($"AddCollidersToChildren: added {added}, repaired {fixedEmpty}, removed {removed} empty colliders.", this);
    }

    bool ShouldSkip(string objName)
    {
        string n = objName.ToLowerInvariant();
        foreach (var s in skipNameContains)
            if (!string.IsNullOrEmpty(s) && n.Contains(s.ToLowerInvariant())) return true;
        return false;
    }

    // --- Undo-aware helpers so you can Ctrl+Z in the editor ---
    static MeshCollider Add(GameObject go)
    {
#if UNITY_EDITOR
        return Undo.AddComponent<MeshCollider>(go);
#else
        return go.AddComponent<MeshCollider>();
#endif
    }

    static void Record(Object o)
    {
#if UNITY_EDITOR
        Undo.RecordObject(o, "Fix MeshCollider");
#endif
    }

    static void Remove(Object o)
    {
#if UNITY_EDITOR
        Undo.DestroyObjectImmediate(o);
#else
        Object.Destroy(o);
#endif
    }
}
