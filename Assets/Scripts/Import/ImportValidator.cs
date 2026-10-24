using System;
using System.IO;
using System.Text;

namespace StageReadyVR.Import
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string Message { get; private set; }
        public ImportKind Kind { get; private set; }
        public string DisplayName { get; private set; }
        public byte[] Data { get; private set; }

        public static ValidationResult Fail(string message)
        {
            return new ValidationResult { IsValid = false, Message = message };
        }

        public static ValidationResult Ok(ImportKind kind, string displayName, byte[] data)
        {
            return new ValidationResult { IsValid = true, Kind = kind, DisplayName = displayName, Data = data };
        }
    }

    /// <summary>
    /// Pure checks on an incoming file. Nothing here touches the disk, so the
    /// same rules protect every entry point and are easy to unit test.
    /// </summary>
    public static class ImportValidator
    {
        public const int MaxScriptBytes = 1024 * 1024;
        public const int MaxPresentationBytes = 25 * 1024 * 1024;
        public const int MaxDisplayNameLength = 80;

        const int PdfHeaderWindow = 1024;
        const int PdfTrailerWindow = 1024;
        static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-");
        static readonly byte[] PdfTrailer = Encoding.ASCII.GetBytes("%%EOF");

        // strict decoding so malformed bytes throw instead of becoming U+FFFD
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static int MaxBytesFor(ImportKind kind)
        {
            return kind == ImportKind.Script ? MaxScriptBytes : MaxPresentationBytes;
        }

        /// <summary>
        /// Reduces a client supplied name to a plain file name. The result is
        /// only ever shown to the user, never used as a path on disk.
        /// </summary>
        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            int cut = Math.Max(raw.LastIndexOf('/'), raw.LastIndexOf('\\'));
            string name = cut >= 0 ? raw.Substring(cut + 1) : raw;

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsControl(c)) continue;
                if (c == '<' || c == '>' || c == ':' || c == '"' || c == '|' || c == '?' || c == '*') continue;
                sb.Append(c);
            }

            name = sb.ToString().Trim().Trim('.');
            if (name.Length <= MaxDisplayNameLength) return name;

            string ext = Path.GetExtension(name);
            if (ext.Length > 10) return string.Empty;
            return name.Substring(0, MaxDisplayNameLength - ext.Length) + ext;
        }

        public static bool TryGetKind(string fileName, out ImportKind kind)
        {
            kind = ImportKind.Script;
            string ext = Path.GetExtension(SanitizeName(fileName)).ToLowerInvariant();
            if (ext == ".txt") { kind = ImportKind.Script; return true; }
            if (ext == ".pdf") { kind = ImportKind.Presentation; return true; }
            return false;
        }

        public static ValidationResult Validate(byte[] data, string originalName)
        {
            string displayName = SanitizeName(originalName);
            if (displayName.Length == 0)
                return ValidationResult.Fail("The file has no usable name.");

            if (!TryGetKind(displayName, out ImportKind kind))
                return ValidationResult.Fail("Only .txt and .pdf files can be imported.");

            if (data == null || data.Length == 0)
                return ValidationResult.Fail("The file is empty.");

            int limit = MaxBytesFor(kind);
            if (data.Length > limit)
                return ValidationResult.Fail("The file is too large. The limit is " + (limit / (1024 * 1024)) + " MB for this file type.");

            return kind == ImportKind.Script
                ? ValidateScript(data, displayName)
                : ValidatePresentation(data, displayName);
        }

        /// <summary>Decodes stored script bytes, or returns false when they are not valid text.</summary>
        public static bool TryDecodeText(byte[] data, out string text)
        {
            text = null;
            try
            {
                text = StrictUtf8.GetString(data);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        static ValidationResult ValidateScript(byte[] data, string displayName)
        {
            // drop the utf-8 byte order mark so stored text starts with real content
            byte[] body = data;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                body = new byte[data.Length - 3];
                Buffer.BlockCopy(data, 3, body, 0, body.Length);
            }

            // nul bytes mean binary or utf-16 text, which would render as garbage
            if (Array.IndexOf(body, (byte)0) >= 0)
                return ValidationResult.Fail("The text file is not valid UTF-8. Save it as UTF-8 and try again.");

            if (!TryDecodeText(body, out string text))
                return ValidationResult.Fail("The text file is not valid UTF-8. Save it as UTF-8 and try again.");

            if (text.Trim().Length == 0)
                return ValidationResult.Fail("The text file is empty.");

            return ValidationResult.Ok(ImportKind.Script, displayName, body);
        }

        static ValidationResult ValidatePresentation(byte[] data, string displayName)
        {
            int headLength = Math.Min(data.Length, PdfHeaderWindow);
            int tailStart = Math.Max(0, data.Length - PdfTrailerWindow);

            if (IndexOf(data, PdfHeader, 0, headLength) < 0 || IndexOf(data, PdfTrailer, tailStart, data.Length - tailStart) < 0)
                return ValidationResult.Fail("This does not look like a valid PDF. Export the presentation as PDF and try again.");

            return ValidationResult.Ok(ImportKind.Presentation, displayName, data);
        }

        static int IndexOf(byte[] haystack, byte[] needle, int start, int count)
        {
            int last = start + count - needle.Length;
            for (int i = start; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
