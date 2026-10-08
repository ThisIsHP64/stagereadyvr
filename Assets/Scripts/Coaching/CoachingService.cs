using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using StageReadyVR.Feedback;

namespace StageReadyVR.Coaching
{
    /// <summary>
    /// Parsed coaching feedback returned by the LLM.
    /// </summary>
    [Serializable]
    public class CoachingFeedback
    {
        public string[] strengths;
        public string[] recommendations;
        public string summary;
        public bool ok;          // false if the call failed
        public string error;     // populated when ok == false
    }

    /// <summary>
    /// Sends a finished SessionReport (metrics + transcript) to the Anthropic
    /// API and returns structured coaching feedback. Attach to a GameObject and
    /// call GetCoaching(report, onComplete).
    ///
    /// The API is asked to return strict JSON so the response parses cleanly
    /// into CoachingFeedback rather than free text.
    /// </summary>
    public class CoachingService : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Anthropic model id")]
        public string model = "claude-sonnet-4-6";
        [Tooltip("Max tokens for the coaching response")]
        public int maxTokens = 1024;

        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";

        /// <summary>
        /// Request coaching for a session. onComplete fires on the main thread
        /// with the parsed feedback (check .ok before using).
        /// </summary>
        public void GetCoaching(SessionReport report, Action<CoachingFeedback> onComplete)
        {
            StartCoroutine(GetCoachingRoutine(report, onComplete));
        }

        private IEnumerator GetCoachingRoutine(SessionReport report, Action<CoachingFeedback> onComplete)
        {
            if (!ApiConfig.HasKey)
            {
                onComplete?.Invoke(Fail("No API key configured."));
                yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            string body = BuildRequestBody(report);
            Debug.Log("[CoachingService] Request body:\n" + body);

            using (var req = new UnityWebRequest(Endpoint, "POST"))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-api-key", ApiConfig.AnthropicKey);
                req.SetRequestHeader("anthropic-version", ApiVersion);

                req.timeout = 30;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[CoachingService] Request failed: {req.error}\n{req.downloadHandler.text}");
                    Debug.Log($"[Timing] CoachingFailed rtt={Time.realtimeSinceStartup - t0:F2}s error={req.error}");
                    onComplete?.Invoke(Fail(req.error));
                    yield break;
                }
                Debug.Log($"[Timing] CoachingResponse rtt={Time.realtimeSinceStartup - t0:F2}s chars={req.downloadHandler.text.Length}");
                Debug.Log("[CoachingService] Raw response:\n" + req.downloadHandler.text);
                CoachingFeedback parsed = ParseResponse(req.downloadHandler.text);
                onComplete?.Invoke(parsed);
            }
        }

        // ── Request construction ──────────────────────────────────────

        private string BuildRequestBody(SessionReport r)
        {
            // The metrics + transcript the model reasons over.
            string sessionJson = BuildSessionJson(r);

            string system =
                "You are a supportive, specific public-speaking coach analyzing a VR " +
                "practice session. You receive delivery metrics, the session context " +
                "(room, audience mode, scenario), and the full transcript.\\n\\n" +
                "GROUNDING REQUIREMENT - this is critical: every strength and every " +
                "recommendation MUST cite concrete evidence. Reference a specific metric " +
                "value (e.g. 'your pace of 138 wpm'), a transcript excerpt (quote the " +
                "speaker's actual words), or a specific behavior. Do NOT give generic " +
                "advice that could apply to anyone. If you cannot ground a point in the " +
                "data provided, leave it out. A point like 'work on your confidence' is " +
                "forbidden; 'you said \\\"um\\\" 6 times, clustered in your opening' is the " +
                "standard.\\n\\n" +
                "Interpret metrics in light of the context: a distracting audience or a " +
                "large auditorium makes maintained eye contact and composure more " +
                "impressive than in an empty room.\\n\\n" +
                "Be encouraging but honest. Respond with ONLY valid JSON, no markdown, in " +
                "exactly this shape: " +
                "{\"summary\":\"...\",\"strengths\":[\"...\"],\"recommendations\":[\"...\"]}. " +
                "Structure your response as: a 'summary' of 1-3 short sentences, then " +
                "'strengths' as 2-3 brief bullet points (one line each, under 18 words), " +
                "then 'recommendations' as 2-3 brief bullet points (one line each, under 18 words). " +
                "Each bullet must cite a specific metric or quote. Be concise — this displays on a VR screen.";

            string userContent =
                "Here is the session data:\\n" + EscapeForJson(sessionJson);

            // Build the Anthropic messages request.
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"model\":\"").Append(model).Append("\",");
            sb.Append("\"max_tokens\":").Append(maxTokens).Append(",");
            sb.Append("\"system\":\"").Append(EscapeForJson(system)).Append("\",");
            sb.Append("\"messages\":[{\"role\":\"user\",\"content\":\"")
              .Append(userContent).Append("\"}]");
            sb.Append("}");
            return sb.ToString();
        }

        // Human-readable metrics block embedded in the prompt.
        private string BuildSessionJson(SessionReport r)
        {
            var s = r.scores;
            var sb = new StringBuilder();
            sb.Append("=== SESSION CONTEXT ===\\n");
            sb.Append("Room / environment: ").Append(r.environmentName).Append("\\n");
            sb.Append("Audience mode: ").Append(string.IsNullOrEmpty(r.audienceMode) ? "unspecified" : r.audienceMode).Append("\\n");
            sb.Append("Scenario: ").Append(string.IsNullOrEmpty(r.scenario) ? "general practice" : r.scenario).Append("\\n");
            sb.Append("Duration (s): ").Append(Mathf.RoundToInt(r.durationSeconds)).Append("\\n\\n");
            sb.Append("=== DELIVERY METRICS ===\\n");
            sb.Append("Words: ").Append(r.totalWords).Append("\\n");
            sb.Append("Pace (WPM): ").Append(Mathf.RoundToInt(r.averageWpm)).Append("\\n");
            sb.Append("Filler words: ").Append(r.fillerWordCount).Append("\\n");
            sb.Append("Long pauses: ").Append(r.longPauseCount).Append("\\n");
            sb.Append("Eye contact (%): ").Append(Mathf.RoundToInt(r.eyeContactRatio * 100)).Append("\\n");
            sb.Append("Head stability (0-1): ").Append(r.headStability.ToString("F2")).Append("\\n");
            sb.Append("Scores - overall:").Append(Mathf.RoundToInt(s.overall))
              .Append(" pace:").Append(Mathf.RoundToInt(s.pace))
              .Append(" filler:").Append(Mathf.RoundToInt(s.fillerControl))
              .Append(" eye:").Append(Mathf.RoundToInt(s.eyeContact))
              .Append(" composure:").Append(Mathf.RoundToInt(s.composure)).Append("\\n\\n");
            sb.Append("=== TRANSCRIPT ===\\n");
            sb.Append(r.transcript);
            return sb.ToString();
        }

        // ── Response parsing ──────────────────────────────────────────

        private CoachingFeedback ParseResponse(string raw)
        {
            try
            {
                // Anthropic returns { "content": [ { "type":"text","text":"..." } ] }.
                // Extract the text block, which itself contains our JSON.
                string text = ExtractTextBlock(raw);
                if (string.IsNullOrEmpty(text))
                    return Fail("Empty response text.");

                // Strip any accidental markdown fences.
                text = text.Replace("```json", "").Replace("```", "").Trim();

                var fb = JsonUtility.FromJson<CoachingFeedback>(text);
                if (fb == null) return Fail("Could not parse coaching JSON.");
                fb.ok = true;
                return fb;
            }
            catch (Exception e)
            {
                return Fail("Parse error: " + e.Message);
            }
        }

        // Minimal extraction of the first content text field from the API JSON.
        private string ExtractTextBlock(string json)
        {
            const string marker = "\"text\":\"";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;

            var sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(next); break;
                    }
                    i++;
                    continue;
                }
                if (c == '"') break;   // end of the text string
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string EscapeForJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }

        private static CoachingFeedback Fail(string msg)
        {
            Debug.LogWarning("[CoachingService] " + msg);
            return new CoachingFeedback
            {
                ok = false,
                error = msg,
                strengths = new string[0],
                recommendations = new string[0],
                summary = ""
            };
        }
    }
}