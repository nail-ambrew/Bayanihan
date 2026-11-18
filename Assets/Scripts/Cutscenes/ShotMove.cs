using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// One camera move in a cutscene: this camera glides from one pose to another while the Timeline is between
/// <see cref="start"/> and <see cref="end"/> (seconds). Driven by the director's time, so scrubbing, skipping and
/// retiming all stay exact. Put on a CinemachineCamera with no Body or Aim of its own; the builders write the poses from
/// their beat tables (rebuild to retime), or set them in the Inspector.
/// </summary>
[ExecuteAlways]
public class ShotMove : MonoBehaviour
{
    public PlayableDirector director;
    public double start, end = 3;
    public Vector3 fromPosition, toPosition;
    public Vector3 fromEuler, toEuler;
    [Tooltip("Look at this the whole shot, instead of the rotations above.")]
    public Transform lookAt;
    public Vector3 lookOffset;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    /// <summary>When set (the intro's last shot), the move ends on this pose instead: the gameplay camera's.</summary>
    public bool overrideEnd;
    public Vector3 endPosition;
    public Quaternion endRotation = Quaternion.identity;

    public float Progress
    {
        get
        {
            if (director == null || end <= start) return 0f;
            return Mathf.Clamp01((float)((director.time - start) / (end - start)));
        }
    }

    void LateUpdate() => Apply();

    public void Apply()
    {
        float k = ease != null ? ease.Evaluate(Progress) : Progress;
        Vector3 to = overrideEnd ? endPosition : toPosition;
        Vector3 p = Vector3.LerpUnclamped(fromPosition, to, k);
        Quaternion rot;
        if (lookAt != null && !overrideEnd) rot = Look(p);
        else
        {
            var from = lookAt != null ? Look(p) : Quaternion.Euler(fromEuler);
            var target = overrideEnd ? endRotation : Quaternion.Euler(toEuler);
            rot = Quaternion.Slerp(from, target, k);
        }
        transform.SetPositionAndRotation(p, rot);
    }

    Quaternion Look(Vector3 from)
    {
        var d = lookAt.position + lookOffset - from;
        return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized, Vector3.up) : transform.rotation;
    }
}
