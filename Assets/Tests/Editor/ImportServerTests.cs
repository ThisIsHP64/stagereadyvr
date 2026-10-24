using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;

namespace StageReadyVR.Import.Tests
{
    public class ImportServerTests
    {
        string dir;
        ImportStore store;
        ImportServer server;
        HttpClient client;
        string baseUrl;

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(UnityEngine.Application.temporaryCachePath, "import-server-tests-" + Guid.NewGuid().ToString("N"));
            store = new ImportStore(dir);
            server = new ImportServer(store);
            Assert.IsTrue(server.Start("127.0.0.1", FreePort()), server.StartError);
            client = new HttpClient();
            baseUrl = "http://127.0.0.1:" + server.Port;
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
            server.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        HttpResponseMessage Upload(string name, byte[] body, string pin = null, Action<HttpRequestMessage> tweak = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/upload?name=" + Uri.EscapeDataString(name))
            {
                Content = new ByteArrayContent(body)
            };
            request.Headers.Add("X-Import-Pin", pin ?? server.Pin);
            tweak?.Invoke(request);
            return client.SendAsync(request).GetAwaiter().GetResult();
        }

        static byte[] Text(string s) { return new UTF8Encoding(false).GetBytes(s); }

        [Test]
        public void ServesUploadPageWithSecurityHeaders()
        {
            HttpResponseMessage r = client.GetAsync(baseUrl + "/").GetAwaiter().GetResult();
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.IsTrue(r.Headers.Contains("Content-Security-Policy"));
            Assert.AreEqual("nosniff", string.Join(",", r.Headers.GetValues("X-Content-Type-Options")));

            string html = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            StringAssert.DoesNotContain("__MAX_", html);
            StringAssert.Contains(ImportValidator.MaxPresentationBytes.ToString(), html);
        }

        [Test]
        public void PinIsSixDigits()
        {
            StringAssert.IsMatch("^[0-9]{6}$", server.Pin);
        }

        [Test]
        public void ValidUploadIsStoredAndRaisesEvent()
        {
            ImportedFile raised = null;
            server.FileImported += f => raised = f;

            HttpResponseMessage r = Upload("speech.txt", Text("Hello."));

            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.AreEqual(1, store.List().Count);
            Assert.IsNotNull(raised);
            Assert.AreEqual("speech.txt", raised.displayName);
        }

        [Test]
        public void ValidPdfUploadIsStored()
        {
            HttpResponseMessage r = Upload("deck.pdf", ImportValidatorTests.MinimalPdf());
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.AreEqual(ImportKind.Presentation, store.List()[0].kind);
        }

        [Test]
        public void WrongPinIsRejectedAndNothingStored()
        {
            string failure = null;
            server.UploadFailed += m => failure = m;

            HttpResponseMessage r = Upload("a.txt", Text("hi"), "000000" == server.Pin ? "111111" : "000000");

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(0, store.List().Count);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void LocksOutAfterRepeatedWrongPins()
        {
            string wrong = "000000" == server.Pin ? "111111" : "000000";
            for (int i = 0; i < 5; i++) Upload("a.txt", Text("hi"), wrong);

            HttpResponseMessage r = Upload("a.txt", Text("hi"));    // correct pin, still refused

            Assert.AreEqual((HttpStatusCode)429, r.StatusCode);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void MismatchedHostIsRejected()
        {
            HttpResponseMessage r = Upload("a.txt", Text("hi"), null, req => req.Headers.Host = "evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void CrossSiteOriginIsRejected()
        {
            HttpResponseMessage r = Upload("a.txt", Text("hi"), null, req => req.Headers.Add("Origin", "http://evil.example"));
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void SameOriginIsAccepted()
        {
            HttpResponseMessage r = Upload("a.txt", Text("hi"), null, req => req.Headers.Add("Origin", baseUrl));
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        }

        [Test]
        public void OversizeBodyIsRejectedBeforeStoring()
        {
            var big = new byte[ImportValidator.MaxScriptBytes + 1];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)'a';

            // the server answers before reading the body, so a client still
            // sending may see either the 413 or a reset connection
            try
            {
                HttpResponseMessage r = Upload("big.txt", big);
                Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
            }
            catch (HttpRequestException)
            {
            }

            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void UnsupportedTypeIsRejected()
        {
            HttpResponseMessage r = Upload("notes.docx", Text("hi"));
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        }

        [Test]
        public void InvalidContentGetsAClearMessage()
        {
            HttpResponseMessage r = Upload("blank.txt", Text("   "));
            string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            Assert.AreEqual((HttpStatusCode)422, r.StatusCode);
            StringAssert.Contains("empty", body);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void UnknownRouteIs404()
        {
            HttpResponseMessage r = client.GetAsync(baseUrl + "/admin").GetAwaiter().GetResult();
            Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        }

        [Test]
        public void StopClosesThePort()
        {
            server.Stop();
            Assert.IsFalse(server.IsRunning);
            Assert.Throws<HttpRequestException>(() => client.GetAsync(baseUrl + "/").GetAwaiter().GetResult());
        }
    }
}
