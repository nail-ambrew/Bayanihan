using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The table at the Barangay Hall where each resident who comes ashore is written on the list. They are seen to one
/// at a time, in the order they landed, and each takes <see cref="secondsPerResident"/>. A resident only counts as
/// safe, and only scores, once their name is on the list - so a boatload takes a while to clear, and a mission is not
/// over the moment the boat touches the porch.
///
/// Added to the EvacuationCenter automatically. The RescueManager is told as each one is registered.
/// </summary>
public class RegistrationDesk : MonoBehaviour
{
    [Tooltip("How long it takes to register one resident (seconds).")]
    public float secondsPerResident = 2f;

    /// <summary>Who is being written down right now, or null.</summary>
    public ResidentRecord Current { get; private set; }
    /// <summary>0 to 1: how far through the person at the desk is.</summary>
    public float Progress01 => Current != null && secondsPerResident > 0f ? Mathf.Clamp01(elapsed / secondsPerResident) : 0f;
    /// <summary>Waiting their turn, the next in line first.</summary>
    public IReadOnlyList<ResidentRecord> Waiting => queue;
    /// <summary>Everybody the desk still has to see: those waiting and the one at the desk.</summary>
    public int Pending => queue.Count + (Current != null ? 1 : 0);
    /// <summary>How many have been registered here this mission.</summary>
    public int RegisteredCount { get; private set; }

    readonly List<ResidentRecord> queue = new List<ResidentRecord>();
    float elapsed;

    /// <summary>A resident has come ashore and joins the end of the line.</summary>
    public void Enqueue(ResidentRecord record)
    {
        if (record == null || record == Current || queue.Contains(record)) return;
        queue.Add(record);
    }

    void Update()
    {
        if (Current == null)
        {
            if (queue.Count == 0) return;
            Current = queue[0];
            queue.RemoveAt(0);
            elapsed = 0f;
        }

        elapsed += Time.deltaTime;
        if (elapsed < secondsPerResident) return;

        var done = Current;
        Current = null;
        elapsed = 0f;
        RegisteredCount++;
        if (RescueManager.Instance != null) RescueManager.Instance.OnRegistered(done);
    }
}
