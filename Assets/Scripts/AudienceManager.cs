using System.Collections.Generic;
using UnityEngine;

public enum AudienceMode { Supportive, Distracting, Mixed }

/// <summary>
/// Assigns a single shared AnimatorController to every NPC, then sets a per-NPC
/// "Behavior" int parameter that the AudienceStateMachine uses to pick its
/// transition pool. In Mixed mode each NPC rolls its own behavior, so the room
/// is a genuine blend rather than one mode for everyone.
/// Behavior values: 0 = Supportive, 1 = Distracting.
///
/// Works in two room types:
///   - Seater rooms: AudienceSeater spawns NPCs at runtime and calls
///     ApplyAudienceMode(animators) with the spawned set.
///   - Manual rooms: NPCs are pre-placed in the scene. AudienceManager finds
///     them itself in Start() by tag and configures them.
/// A configured-set guard prevents an NPC being initialized twice if both
/// paths happen to run.
/// </summary>
public class AudienceManager : MonoBehaviour
{
    public AudienceMode audienceMode = AudienceMode.Supportive;

    [Tooltip("Single shared controller with both supportive + distracting states")]
    public RuntimeAnimatorController audienceController;

    [Range(0f, 1f)]
    [Tooltip("In Mixed mode, fraction of NPCs that are distracting")]
    public float mixedDistractingRatio = 0.4f;

    [Tooltip("Tag used to find pre-placed NPCs in rooms without an AudienceSeater")]
    public string npcTag = "AudienceNPC";

    [Tooltip("If true, auto-find pre-placed NPCs by tag at Start (manual rooms)")]
    public bool autoFindPrePlaced = true;

    // NPCs already configured, so we never double-initialize.
    private readonly HashSet<Animator> configured = new HashSet<Animator>();

    void Awake()
    {
        int savedMode = PlayerPrefs.GetInt("AudienceMode", 0);
        if (savedMode == 0) audienceMode = AudienceMode.Supportive;
        else if (savedMode == 1) audienceMode = AudienceMode.Mixed;
        else if (savedMode == 2) audienceMode = AudienceMode.Distracting;
    }

    void Start()
    {
        // Seater rooms call ApplyAudienceMode themselves (also in Start, but the
        // guard makes order irrelevant). Manual rooms rely on this auto-find.
        if (autoFindPrePlaced)
        {
            var found = new List<Animator>();
            GameObject[] tagged = SafeFindByTag(npcTag);
            foreach (var go in tagged)
            {
                var anim = go.GetComponent<Animator>();
                if (anim == null) anim = go.GetComponentInChildren<Animator>();
                if (anim != null) found.Add(anim);
            }
            if (found.Count > 0)
                ApplyAudienceMode(found.ToArray());
        }
    }

    /// <summary>
    /// Configure the given animators. Safe to call from the seater with spawned
    /// NPCs, or from Start with pre-placed ones. Already-configured NPCs are
    /// skipped.
    /// </summary>
    public void ApplyAudienceMode(Animator[] members)
    {
        foreach (Animator npc in members)
        {
            if (npc == null || configured.Contains(npc)) continue;

            npc.runtimeAnimatorController = audienceController;
            npc.applyRootMotion = false;

            int behavior = ResolveBehavior();
            npc.SetInteger("Behavior", behavior);

            // Ensure the per-NPC cycler exists, then start it.
            var bc = npc.GetComponent<AudienceBehaviorController>();
            if (bc == null) bc = npc.gameObject.AddComponent<AudienceBehaviorController>();
            bc.Initialize(behavior);

            configured.Add(npc);
        }
    }

    private int ResolveBehavior()
    {
        switch (audienceMode)
        {
            case AudienceMode.Distracting: return 1;
            case AudienceMode.Mixed: return Random.value < mixedDistractingRatio ? 1 : 0;
            default: return 0; // Supportive
        }
    }

    /// <summary>Called by SessionManager at session end to trigger reactions.</summary>
    public void OnSessionEnded(float performanceScore01)
    {
        foreach (Animator npc in configured)
        {
            if (npc == null) continue;
            npc.SetFloat("PerformanceScore", performanceScore01);
            npc.SetBool("SessionEnded", true);
        }
    }

    private static GameObject[] SafeFindByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return new GameObject[0];
        try { return GameObject.FindGameObjectsWithTag(tag); }
        catch { Debug.LogWarning($"[AudienceManager] Tag '{tag}' is not defined."); return new GameObject[0]; }
    }
}