using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Requests microphone permission at app launch so speech analysis works on
/// the very first session. Place on any GameObject in the main menu scene.
/// </summary>
public class StartupPermissions : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
    }
}