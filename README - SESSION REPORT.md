# StageReadyVR REQ-3 — connect the real SessionReport to GPT-6 Luna

This is an additive integration for the existing *working* REQ-3 live AI service. It **does not** replace `SessionManager.cs`, `SessionReport.cs`, `FeedbackController.cs`, `DashboardPanel.cs`, the Anthropic coaching code, or the original Unity Editor live test.

Verified integration points on public `master` source:
- `SessionManager.EndSession()` stops trackers and calls `feedbackController?.EndSession();`.
- `FeedbackController.EndSession()` finalizes metrics and stores a report at `CurrentReport`.
- `SessionReportScoringAdapter.FromReport()` (from your existing REQ-3 package) maps actual session data to REQ-3 fields.

> **Local version warning:** You intentionally did not pull your teammate's latest commits. The auto-patcher looks for exact code anchors and stops if they differ; it does not replace changed team files.

## Install safely on Windows

1. Close Unity and back up your `C:\stagereadyvr` project (or your four source files).
2. Extract the ZIP, e.g., to your Downloads folder.
3. Open **Windows PowerShell**, navigate to the extracted folder, and run:

   ```powershell
   cd "C:\path\to\extracted\StageReadyVR_REQ3_SessionReport_Integration"
   & .\INSTALL-REQ3.ps1
   ```

   If Windows blocks PowerShell scripts, read `PATCH_SNIPPETS.md` and copy the new `.cs` files into their specified paths manually. Do not change your computer's security policy just for this package.

4. The installer writes timestamped backups **outside the repository** to your Windows TEMP folder, copies **three new C# files**, and adds small code blocks to four existing scripts. No Git operations are performed. It is repeat-safe for already patched methods, but will refuse overwriting existing new scripts.
5. Reopen Unity, allow compilation, and check **Window > General > Console** for red errors.

### What changes

- `SessionManager.EndSession`: adds one call after `FeedbackController.EndSession`.
- `SessionReport`: adds `req3AiScores`, `req3AiStatus`, `req3AiError`.
- `FeedbackController` and `DashboardPanel`: display live AI scores or a clear failure status in the existing summary area. Existing bar scores stay the same.
- New `REQ3SessionScoringBridge`: converts the finalized report, scores it asynchronously, updates the report and persists JSON with the same filename the existing `FeedbackController` uses. A runner survives scene navigation and ignores duplicate session calls.
- New `REQ3EditorSessionKeyWindow`: keeps the development API key **only in the Unity Editor process** (not in a scene, ZIP, or source file).
- New `REQ3FinalizedReportTest`: tests the full SessionReport mapping/saving pipeline in Play Mode without a headset.

## Test in Unity (no headset or 3D environment required)

1. Choose **StageReadyVR > REQ-3 > Set Editor Session API Key**. Paste your **replacement** OpenAI API key; click **Use key for this Unity Editor session**. Never paste it into chat or commit it. You previously shared a key in chat; revoke that one before proceeding.
2. Stay on the empty Untitled scene. Click **Play** at the top of Unity.
3. Choose **StageReadyVR > REQ-3 > Test Finalized SessionReport (Play Mode)**. This builds a **synthetic** but real `SessionReport` object and passes it through the same bridge that `SessionManager.EndSession` will call.
4. In Console look for `SESSION REPORT SAVED — GPT-6 Luna SUCCESS`. The test makes a genuine API request and may incur charges.
5. The Console logs the JSON save path under `Application.persistentDataPath/sessions`. Open that `session_*.json` file and verify the new `req3AiScores`, `req3AiStatus`, and `req3AiError` fields. For a success, `req3AiStatus` should be `completed` and `req3AiScores` should contain four numbers and feedback.
6. Click Stop (exit Play Mode). For an actual session, when a `SessionManager` reaches `EndSession`, it calls the exact same bridge using `feedbackController.CurrentReport`.

If there is **no transcript** yet, the bridge marks the report `failed` with a human-readable error instead of crashing. The four scores cannot be generated reliably without the report's content.

### Expected saved JSON excerpt (example only)

```json
"req3AiScores": {
  "eyeContact": 82,
  "fillerWords": 70,
  "confidence": 79,
  "speechPacing": 88,
  "feedback": "Rehearse transitions to reduce filler words."
},
"req3AiStatus": "completed",
"req3AiError": ""
```

## Limits and security

- The new automatic live call works **only in the Unity Editor**. For an actual PICO 4 APK, the bridge explicitly reports that a **secure backend relay** is needed; an API key must never be embedded in a headset app. This is a necessary production follow-up, not a completed APK integration.
- Your previous **Live AI Scoring (No VR)** window is still available and unchanged, but the key entered there is for that test window. Automatic Play Mode scoring uses the new **Set Editor Session API Key** menu.
- The scorer does not change the deterministic `SessionReport.scores` or the Anthropic coaching. AI results are separate fields.
- The existing dashboard summary gets a basic appended REQ-3 status line. The team should add a dedicated scrollable section for polished VR display later if the text gets clipped.
- The original `FeedbackController` also persists the JSON report. If it finishes coaching later, it writes the same report object including REQ-3 fields. Avoid ending multiple sessions within the same second because the existing timestamp filename could collide.
- Because you did not pull teammates' latest changes, confirm the four patch anchor points still match your own local scripts. The helper refuses unknown layouts rather than overwriting them.
- No headset, Unity compiler, or authenticated API were available in the packaging environment to execute this integration end-to-end. Test in your local Unity Editor.
