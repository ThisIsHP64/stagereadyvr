using System.Text;
using NUnit.Framework;

namespace StageReadyVR.Import.Tests
{
    public class ImportValidatorTests
    {
        static byte[] Text(string s) { return new UTF8Encoding(false).GetBytes(s); }

        public static byte[] MinimalPdf()
        {
            return Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        }

        [Test]
        public void AcceptsPlainText()
        {
            ValidationResult r = ImportValidator.Validate(Text("Good morning everyone."), "speech.txt");
            Assert.IsTrue(r.IsValid, r.Message);
            Assert.AreEqual(ImportKind.Script, r.Kind);
            Assert.AreEqual("speech.txt", r.DisplayName);
        }

        [Test]
        public void AcceptsUppercaseExtension()
        {
            Assert.IsTrue(ImportValidator.Validate(Text("hello"), "SPEECH.TXT").IsValid);
        }

        [Test]
        public void StripsUtf8Bom()
        {
            var withBom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
            ValidationResult r = ImportValidator.Validate(withBom, "a.txt");
            Assert.IsTrue(r.IsValid);
            Assert.AreEqual(2, r.Data.Length);
        }

        [Test]
        public void AcceptsNonAsciiText()
        {
            Assert.IsTrue(ImportValidator.Validate(Text("Caf\u00e9 \u4f60\u597d"), "a.txt").IsValid);
        }

        [Test]
        public void AcceptsPdf()
        {
            ValidationResult r = ImportValidator.Validate(MinimalPdf(), "slides.pdf");
            Assert.IsTrue(r.IsValid, r.Message);
            Assert.AreEqual(ImportKind.Presentation, r.Kind);
        }

        [TestCase("notes.docx")]
        [TestCase("slides.pptx")]
        [TestCase("script")]
        [TestCase(".txt")]
        [TestCase("")]
        [TestCase(null)]
        public void RejectsUnsupportedOrMissingNames(string name)
        {
            Assert.IsFalse(ImportValidator.Validate(Text("hello"), name).IsValid);
        }

        [Test]
        public void RejectsEmptyAndWhitespaceText()
        {
            Assert.IsFalse(ImportValidator.Validate(new byte[0], "a.txt").IsValid);
            Assert.IsFalse(ImportValidator.Validate(null, "a.txt").IsValid);
            Assert.IsFalse(ImportValidator.Validate(Text("  \r\n\t "), "a.txt").IsValid);
        }

        [Test]
        public void RejectsInvalidUtf8()
        {
            Assert.IsFalse(ImportValidator.Validate(new byte[] { 0xFF, 0xFE, 0x41 }, "a.txt").IsValid);
        }

        [Test]
        public void RejectsNulBytes()
        {
            Assert.IsFalse(ImportValidator.Validate(new byte[] { (byte)'a', 0, (byte)'b' }, "a.txt").IsValid);
        }

        [Test]
        public void RejectsOversizeText()
        {
            var big = new byte[ImportValidator.MaxScriptBytes + 1];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)'a';
            Assert.IsFalse(ImportValidator.Validate(big, "a.txt").IsValid);
        }

        [Test]
        public void RejectsOversizePdf()
        {
            var big = new byte[ImportValidator.MaxPresentationBytes + 1];
            Assert.IsFalse(ImportValidator.Validate(big, "a.pdf").IsValid);
        }

        [Test]
        public void RejectsRenamedNonPdf()
        {
            Assert.IsFalse(ImportValidator.Validate(Text("just some text"), "fake.pdf").IsValid);
        }

        [Test]
        public void RejectsTruncatedPdf()
        {
            byte[] noTrailer = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\nendobj\n");
            Assert.IsFalse(ImportValidator.Validate(noTrailer, "cut.pdf").IsValid);
        }

        [TestCase("../../etc/passwd.txt", "passwd.txt")]
        [TestCase("..\\..\\boot.txt", "boot.txt")]
        [TestCase("C:\\Users\\me\\speech.txt", "speech.txt")]
        [TestCase("sp\neech<>.txt", "speech.txt")]
        public void SanitizesNames(string raw, string expected)
        {
            Assert.AreEqual(expected, ImportValidator.SanitizeName(raw));
        }

        [Test]
        public void CapsLongNamesButKeepsExtension()
        {
            string name = ImportValidator.SanitizeName(new string('a', 300) + ".txt");
            Assert.AreEqual(ImportValidator.MaxDisplayNameLength, name.Length);
            Assert.IsTrue(name.EndsWith(".txt"));
        }
    }
}
