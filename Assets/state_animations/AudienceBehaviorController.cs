using System.Collections;
using UnityEngine;

/// <summary>
/// Per-NPC idle variety driver. Picks a fresh random idle within the NPC's
/// assigned behavior pool every few seconds, with a randomized start offset so
/// NPCs don't move in unison. Works with a single AudienceStateMachine that
/// branches on the "Behavior" int and selects a clip via "RandomIdle".
/// </summary>
[RequireComponent(typeof(Animator))]
public class AudienceBehaviorController : MonoBehaviour
{
    [Tooltip("Seconds between idle re-rolls (randomized within this range)")]
    public Vector2 cycleInterval = new Vector2(8f, 15f);

    [Tooltip("Number of supportive idle clips (Behavior 0)")]
    public int supportiveVariants = 4;

    [Tooltip("Number of distracting idle clips (Behavior 1)")]
    public int distractingVariants = 6;

    private Animator animator;
    private int behavior;        // 0 supportive, 1 distracting
    private bool initialized;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>Called by AudienceManager after assigning the controller.</summary>
    public void Initialize(int behaviorValue)
    {
        behavior = behaviorValue;
        initialized = true;
        StopAllCoroutines();
        StartCoroutine(CycleIdles());
    }

    private IEnumerator CycleIdles()
    {
        // Desync NPCs so they don't all re-roll on the same frame.
        yield return new WaitForSeconds(Random.Range(0f, cycleInterval.y));

        while (initialized)
        {
            int poolSize = (behavior == 1) ? distractingVariants : supportiveVariants;
            int idle = Random.Range(0, poolSize);
            animator.SetInteger("RandomIdle", idle);
            animator.SetTrigger("Reroll");

            float wait = Random.Range(cycleInterval.x, cycleInterval.y);
            yield return new WaitForSeconds(wait);
        }
    }
}