from pathlib import Path
import sys
import assemblyai as aai

key_path = Path("Assets/Resources/assemblyai_key.txt")
aai.settings.api_key = key_path.read_text().strip()

audio_file = sys.argv[1]

print("Sending recording to AssemblyAI...")

transcriber = aai.Transcriber()
transcript = transcriber.transcribe(audio_file)

if transcript.status == aai.TranscriptStatus.error:
    raise RuntimeError(transcript.error)

print("\n--- STAGEREADYVR TRANSCRIPT ---")
print(transcript.text)

Path("test_transcript.txt").write_text(
    transcript.text or ""
)

print("\nTranscript saved successfully!")