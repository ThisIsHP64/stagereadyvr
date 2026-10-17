#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

/// <summary>
/// One-shot migration from the Meta Quest rig and Interaction SDK ray UI to the
/// PICO XR Origin and XR Interaction Toolkit UI, plus replacing the Wit
/// dictation object with the AssemblyTranscriber stub.
///
/// Meta types are matched by name instead of by reference, so this compiles
/// both before and after the Meta packages are removed. Run it BEFORE removing
/// them, otherwise the scenes only keep "missing script" entries and the
/// original wiring is lost.
///
/// Prerequisites:
///   1. PICO Unity Integration SDK imported.
///   2. XR Interaction Toolkit "Starter Assets" sample imported (Package
///      Manager > XR Interaction Toolkit > Samples). The PICO controller
///      building block needs its input actions and presets.
///
/// Menu: StageReadyVR > Pico Migration. Order: prefabs first, then scenes.
/// Both steps are idempotent and safe to re-run.
/// </summary>
public static class PicoMigrationTool
{
    const string PrefabFolder = "Assets/prefabs_custom_screeens";
    const string ControllerBlockMenu = "PICO/PICO Building Blocks/PICO Controller/PICO Controller Tracking";

    // prefabs that exist only for the Meta SDK and are deleted separately
    static readonly string[] SkippedPrefabs = { "[BuildingBlock] Camera Rig", "App Dictation Experience" };

    // ordered so dependents are removed before the components they point at
    static readonly string[] MetaComponentNames =
    {
        "InteractableUnityEventWrapper", "PointableUnityEventWrapper", "InteractableDebugVisual",
        "RayInteractable", "PointableCanvas", "PointableCanvasModule",
        "RectTransformBoundsClipperDriver", "BoundsClipper", "ClippedPlaneSurface",
        "PlaneSurface", "ColliderSurface",
        "OVRControllerHelper", "OVRHeadsetEmulator", "OVRManager", "OVRCameraRig",
        "BuildingBlock", "AppDictationExperience"
    };

    // ── menu entry points ─────────────────────────────────────────────

    [MenuItem("StageReadyVR/Pico Migration/1 Migrate Prefabs")]
    public static void MigratePrefabsMenu()
    {
        int changed = MigratePrefabs();
        EditorUtility.DisplayDialog("Pico migration", $"Prefabs migrated: {changed}. See the Console for details.", "OK");
    }

    [MenuItem("StageReadyVR/Pico Migration/2 Migrate Build Scenes")]
    public static void MigrateScenesMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        bool ok = MigrateScenes();
        EditorUtility.DisplayDialog("Pico migration",
            ok ? "All build scenes migrated. Review the Console, then save and test."
               : "Some scenes failed. See the Console for details.", "OK");
    }

    /// <summary>Command line entry: Unity -batchmode -executeMethod PicoMigrationTool.RunAll</summary>
    public static void RunAll()
    {
        MigratePrefabs();
        bool ok = MigrateScenes();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    // ── prefabs ───────────────────────────────────────────────────────

    static int MigratePrefabs()
    {
        int changed = 0;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (SkippedPrefabs.Contains(name)) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = RemoveMetaComponents(new[] { root }, false) > 0;
                dirty |= EnsureXrEventSystems(root);
                dirty |= EnsureTrackedDeviceRaycasters(root, null);
                dirty |= EnsureTranscribers(root);

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                    Debug.Log($"[PicoMigration] prefab updated: {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PicoMigration] prefab failed: {path}\n{e}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        return changed;
    }

    // ── scenes ────────────────────────────────────────────────────────

    static bool MigrateScenes()
    {
        if (AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset").Length == 0)
        {
            Debug.LogError("[PicoMigration] XRI Starter Assets sample is not imported. " +
                           "Package Manager > XR Interaction Toolkit > Samples > Starter Assets > Import, then re-run.");
            return false;
        }

        bool allOk = true;
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes.Where(s => s.enabled))
        {
            try
            {
                allOk &= MigrateScene(entry.path);
            }
            catch (Exception e)
            {
                allOk = false;
                Debug.LogError($"[PicoMigration] scene failed: {entry.path}\n{e}");
            }
        }
        return allOk;
    }

    static bool MigrateScene(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Debug.Log($"[PicoMigration] migrating scene: {path}");

        // 1. find the old rig and remember where the player started
        GameObject oldRig = FindByComponentName(scene, "OVRCameraRig");
        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        HashSet<Transform> oldTransforms = new HashSet<Transform>();
        if (oldRig != null)
        {
            oldRig = PrefabUtility.GetOutermostPrefabInstanceRoot(oldRig) ?? oldRig;
            oldRig.transform.GetPositionAndRotation(out pos, out rot);
            foreach (Transform t in oldRig.GetComponentsInChildren<Transform>(true)) oldTransforms.Add(t);
        }

        // 2. make sure the PICO XR Origin exists
        XROrigin origin = Object.FindObjectsOfType<XROrigin>(true).FirstOrDefault();
        if (origin == null)
        {
            EditorApplication.ExecuteMenuItem(ControllerBlockMenu);
            origin = Object.FindObjectsOfType<XROrigin>(true).FirstOrDefault();
        }
        if (origin == null)
        {
            Debug.LogError($"[PicoMigration] could not create an XR Origin in {path}. " +
                           $"Run the menu item '{ControllerBlockMenu}' manually in this scene and re-run.");
            return false;
        }
        scene = origin.gameObject.scene;

        if (origin.GetComponent<PXR_Manager>() == null) origin.gameObject.AddComponent<PXR_Manager>();
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        if (oldRig != null) origin.transform.SetPositionAndRotation(pos, rot);

        Camera cam = origin.Camera;
        if (cam == null)
        {
            Debug.LogError($"[PicoMigration] XR Origin has no camera in {path}.");
            return false;
        }
        Transform left = FindController(origin, "Left");
        Transform right = FindController(origin, "Right");

        // 3. point scripts that referenced the old rig at the new one
        if (oldRig != null)
        {
            int remapped = RemapReferences(scene, oldTransforms, origin, cam.transform, left, right);
            Debug.Log($"[PicoMigration] references remapped: {remapped}");
        }

        // 4. swap speech provider before the Meta objects disappear
        RemoveDictationObjects(scene);
        foreach (GameObject root in scene.GetRootGameObjects()) EnsureTranscribers(root);

        // 5. delete the old rig, then strip the remaining Meta components
        if (oldRig != null) Undo.DestroyObjectImmediate(oldRig);
        int removed = RemoveMetaComponents(scene.GetRootGameObjects(), true);
        Debug.Log($"[PicoMigration] meta components removed: {removed}");

        // 6. wire the UI to the XR ray interactors
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            EnsureXrEventSystems(root);
            EnsureTrackedDeviceRaycasters(root, cam);
        }

        ReportMissingScripts(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        return EditorSceneManager.SaveScene(scene);
    }

    // ── rig and reference helpers ─────────────────────────────────────

    static Transform FindController(XROrigin origin, string side)
    {
        Transform offset = origin.CameraFloorOffsetObject != null
            ? origin.CameraFloorOffsetObject.transform
            : origin.transform;

        // names differ between XRI versions ("LeftHand Controller" vs "Left Controller")
        return offset.Cast<Transform>().FirstOrDefault(t =>
            t.name.StartsWith(side, StringComparison.OrdinalIgnoreCase) &&
            t.GetComponent<XRBaseController>() != null);
    }

    static int RemapReferences(Scene scene, HashSet<Transform> oldTransforms, XROrigin origin,
                               Transform camera, Transform left, Transform right)
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (oldTransforms.Contains(root.transform)) continue;

            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || oldTransforms.Contains(mb.transform) || IsMetaComponent(mb)) continue;

                var so = new SerializedObject(mb);
                bool changed = false;
                SerializedProperty prop = so.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object value = prop.objectReferenceValue;
                    Transform old = value is Component c ? c.transform : (value as GameObject)?.transform;
                    if (old == null || !oldTransforms.Contains(old)) continue;

                    Object replacement = Resolve(value, old, origin, camera, left, right);
                    if (replacement == null)
                        Debug.LogWarning($"[PicoMigration] no replacement for '{old.name}' referenced by {mb.GetType().Name} on '{mb.name}'");
                    prop.objectReferenceValue = replacement;
                    changed = true;
                    count++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        return count;
    }

    static Object Resolve(Object value, Transform old, XROrigin origin, Transform camera, Transform left, Transform right)
    {
        Transform target = old.name switch
        {
            "CenterEyeAnchor" or "LeftEyeAnchor" or "RightEyeAnchor" => camera,
            "LeftHandAnchor" or "LeftControllerAnchor" or "LeftHandAnchorDetached" => left,
            "RightHandAnchor" or "RightControllerAnchor" or "RightHandAnchorDetached" => right,
            "TrackingSpace" => origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform,
            _ => old.parent == null ? origin.transform : null
        };
        if (target == null) return null;
        if (value is GameObject) return target.gameObject;
        if (value is Transform) return target;
        return target.GetComponent(value.GetType());
    }

    // ── meta component removal ────────────────────────────────────────

    static bool IsMetaComponent(Component c)
    {
        if (c == null) return false;
        Type t = c.GetType();
        string asm = t.Assembly.GetName().Name;
        return MetaComponentNames.Contains(t.Name) &&
               (asm.StartsWith("Oculus", StringComparison.Ordinal) ||
                asm.StartsWith("Meta", StringComparison.Ordinal) ||
                asm.StartsWith("VoiceSDK", StringComparison.Ordinal));
    }

    static GameObject FindByComponentName(Scene scene, string typeName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == typeName && IsMetaComponent(c)) return c.gameObject;
        return null;
    }

    static int RemoveMetaComponents(IEnumerable<GameObject> roots, bool useUndo)
    {
        int removed = 0;

        // a few passes because some components refuse removal while others depend on them
        for (int pass = 0; pass < 3; pass++)
        {
            List<Component> targets = roots
                .SelectMany(r => r.GetComponentsInChildren<Component>(true))
                .Where(IsMetaComponent)
                .OrderBy(c => Array.IndexOf(MetaComponentNames, c.GetType().Name))
                .ToList();
            if (targets.Count == 0) break;

            int before = removed;
            foreach (Component c in targets)
            {
                if (c == null) continue;
                if (useUndo) Undo.DestroyObjectImmediate(c);
                else Object.DestroyImmediate(c, true);
                if (c == null) removed++;
            }
            if (removed == before) break;
        }
        return removed;
    }

    static void RemoveDictationObjects(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var dictations = root.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == "AppDictationExperience" && IsMetaComponent(c))
                .ToList();

            foreach (Component d in dictations)
            {
                if (d == null) continue;
                GameObject go = PrefabUtility.GetOutermostPrefabInstanceRoot(d.gameObject) ?? d.gameObject;

                // never delete an object that also carries the analyzer
                if (go.GetComponentInChildren<SpeechAnalyzer>(true) != null) continue;
                Undo.DestroyObjectImmediate(go);
            }
        }
    }

    // ── speech, event system, canvases ────────────────────────────────

    static bool EnsureTranscribers(GameObject root)
    {
        bool changed = false;
        foreach (SpeechAnalyzer analyzer in root.GetComponentsInChildren<SpeechAnalyzer>(true))
        {
            if (analyzer.transcriber != null) continue;

            SpeechTranscriber transcriber = analyzer.GetComponentInChildren<SpeechTranscriber>(true);
            if (transcriber == null) transcriber = analyzer.gameObject.AddComponent<AssemblyTranscriber>();

            var so = new SerializedObject(analyzer);
            so.FindProperty("transcriber").objectReferenceValue = transcriber;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        return changed;
    }

    static bool EnsureXrEventSystems(GameObject root)
    {
        bool changed = false;
        foreach (EventSystem es in root.GetComponentsInChildren<EventSystem>(true))
        {
            // only one input module may drive the event system
            foreach (BaseInputModule module in es.GetComponents<BaseInputModule>())
            {
                if (module is XRUIInputModule) continue;
                if (module.enabled) { module.enabled = false; changed = true; }
            }

            XRUIInputModule xr = es.GetComponent<XRUIInputModule>();
            if (xr == null) { es.gameObject.AddComponent<XRUIInputModule>(); changed = true; }
            else if (!xr.enabled) { xr.enabled = true; changed = true; }
        }
        return changed;
    }

    static bool EnsureTrackedDeviceRaycasters(GameObject root, Camera worldCamera)
    {
        bool changed = false;
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode != RenderMode.WorldSpace) continue;

            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                changed = true;
            }
            if (worldCamera != null && canvas.worldCamera != worldCamera)
            {
                canvas.worldCamera = worldCamera;
                changed = true;
            }
        }
        return changed;
    }

    static void ReportMissingScripts(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0)
                    Debug.LogWarning($"[PicoMigration] {missing} missing script(s) on '{t.name}' in {scene.name}", t.gameObject);
            }
    }
}
#endif
