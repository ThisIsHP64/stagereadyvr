using UnityEngine;

public class LookAtTarget : MonoBehaviour
{
    public Transform target;
    public Transform neckBone;
    [Range(0f, 1f)] public float weight = 0.6f; // how strongly head turns
    public float smoothSpeed = 5f;

    private Quaternion _currentRot;

    void LateUpdate()
    {
        if (target == null || neckBone == null) return;

        Quaternion targetRot = Quaternion.LookRotation(target.position - neckBone.position);
        _currentRot = Quaternion.Slerp(_currentRot, targetRot, Time.deltaTime * smoothSpeed);
        neckBone.rotation = Quaternion.Slerp(neckBone.rotation, _currentRot, weight);
    }
}