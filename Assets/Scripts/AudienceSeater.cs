using UnityEngine;
using System.Collections.Generic;

public class AudienceSeater : MonoBehaviour
{
    public GameObject[] npcPrefabs;
    public Transform chairParent;
    public Transform speakerTarget;
    public RuntimeAnimatorController fallbackController;
    public int maxNPCs = 40;
    public float xOffset = 0f;
    public float yOffset = 0.3f;
    public float zOffset = 0f;

    void Start()
    {
        SpawnAudience();
    }

    //void Update()
    //{
    //    if (Input.GetKeyDown(KeyCode.Space))
    //    {
    //        foreach (Transform child in transform)
    //            Destroy(child.gameObject);
    //        SpawnAudience();
    //    }
    //}

    void SpawnAudience()
    {
        if (npcPrefabs.Length == 0 || chairParent == null) return;

        //AudienceManager am = FindObjectOfType<AudienceManager>();
        //Debug.Log("AudienceManager found: " + (am != null));
        //Debug.Log("AudienceMode: " + (am != null ? am.audienceMode.ToString() : "NULL"));


        AudienceManager am = FindObjectOfType<AudienceManager>();

        List<Transform> validSeats = new List<Transform>();
        foreach (Transform seat in chairParent.GetComponentsInChildren<Transform>())
        {
            if (seat != chairParent && seat.name.StartsWith("Chair_01"))
                validSeats.Add(seat);
        }

        int total = validSeats.Count;
        int spawnCount = Mathf.Min(maxNPCs, total);
        float step = (float)total / spawnCount;

        for (int i = 0; i < spawnCount; i++)
        {
            int index = Mathf.FloorToInt(i * step);
            Transform seat = validSeats[index];

            GameObject prefab = npcPrefabs[i % npcPrefabs.Length];
            Vector3 seatPos = seat.position + new Vector3(xOffset, yOffset, zOffset);
            GameObject npc = Instantiate(prefab, seatPos, seat.rotation);
            npc.transform.SetParent(transform);

            Animator animator = npc.GetComponent<Animator>();
            if (animator != null)
            {
                //animator.runtimeAnimatorController = am != null ? am.GetController() : fallbackController;
                animator.applyRootMotion = false;
            }

            LookAtTarget look = npc.GetComponent<LookAtTarget>();
            if (look != null)
            {
                look.target = speakerTarget;
                foreach (Transform t in npc.GetComponentsInChildren<Transform>())
                {
                    if (t.name.Contains("Head"))
                    {
                        look.neckBone = t;
                        break;
                    }
                }
            }
            AudienceBehaviorController abc = npc.GetComponent<AudienceBehaviorController>();
            if (abc == null)
                abc = npc.AddComponent<AudienceBehaviorController>();
        }
        if (am != null)
            am.ApplyAudienceMode(GetComponentsInChildren<Animator>());
    }
}