# Manual patch snippets (only if you prefer not to run INSTALL-REQ3.ps1)

**Back up each file first. Close Unity while editing. Do not overwrite whole existing team files.**

## A. `Assets/Scripts/SessionManager.cs`

Find inside `public void EndSession()`:

```csharp
        feedbackController?.EndSession();
```

Immediately after it add:

```csharp
        if (feedbackController != null)
            StageReadyVR.PerformanceScoring.REQ3SessionScoringBridge.OnSessionEnded(
                feedbackController.CurrentReport, feedbackController);
```

## B. `Assets/Scripts/Feedback/SessionReport.cs`

Find inside `public class SessionReport`:

```csharp
        public SessionScores scores;
```

Immediately after it add:

```csharp
        // Separate AI scores; existing ScoreCalculator data remains untouched.
        public StageReadyVR.PerformanceScoring.PerformanceScores req3AiScores;
        public string req3AiStatus = "not_requested";
        public string req3AiError = "";
```

## C. `Assets/Scripts/Feedback/FeedbackController.cs`

Find:

```csharp
        public SessionReport CurrentReport => _report;
```

Immediately after it add:

```csharp
        public void ShowReq3Status(SessionReport report)
        {
            if (report == _report && dashboard != null)
                dashboard.ShowReq3Status(report);
        }
```

## D. `Assets/Scripts/Feedback/DashboardPanel.cs`

Insert the following method **inside** the `DashboardPanel` class, immediately before `private static string BuildSummary(SessionReport r)`:

```csharp
        public void ShowReq3Status(SessionReport report)
        {
            if (report == null || summaryText == null) return;
            string status = report.req3AiStatus;
            string detail;
            if (status == "completed" && report.req3AiScores != null)
            {
                var s = report.req3AiScores;
                detail = $"GPT-6 Luna: Eye {s.eyeContact}/100 | Fillers {s.fillerWords}/100 | " +
                         $"Confidence {s.confidence}/100 | Pace {s.speechPacing}/100\n" +
                         (s.feedback ?? "").Replace("<", "‹").Replace(">", "›");
            }
            else if (status == "failed")
                detail = "AI scoring unavailable: " + (report.req3AiError ?? "Please try again.");
            else
                detail = "AI performance scoring in progress...";
            summaryText.text = BuildSummary(report) + "\n" + detail;
        }
```

Note: this appends REQ-3 text to the current summary area. If it overflows in the headset dashboard, the team should add a dedicated scrolling text widget later.
