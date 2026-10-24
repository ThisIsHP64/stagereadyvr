namespace StageReadyVR.Import
{
    /// <summary>
    /// The page the headset serves to the user's laptop or phone. It has no
    /// external resources and writes results with textContent, never innerHTML,
    /// so nothing from the network can inject markup.
    /// </summary>
    internal static class UploadPage
    {
        // limits come from the validator so the page and the server cannot drift apart
        public static readonly string Html = Template
            .Replace("__MAX_TXT__", ImportValidator.MaxScriptBytes.ToString())
            .Replace("__MAX_PDF__", ImportValidator.MaxPresentationBytes.ToString());

        const string Template = @"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>StageReadyVR Import</title>
<style>
  body { font-family: system-ui, sans-serif; background: #14161d; color: #f4f6fa; margin: 0; padding: 24px; }
  main { max-width: 480px; margin: 0 auto; }
  h1 { font-size: 22px; margin: 0 0 8px; }
  p { color: #b6bdcc; line-height: 1.4; }
  label { display: block; margin: 18px 0 6px; font-weight: 600; }
  input { width: 100%; box-sizing: border-box; padding: 12px; border-radius: 8px; border: 1px solid #3a4052; background: #1e222d; color: #f4f6fa; font-size: 16px; }
  button { margin-top: 20px; width: 100%; padding: 14px; border: 0; border-radius: 8px; background: #529ef0; color: #08101c; font-size: 16px; font-weight: 700; cursor: pointer; }
  button:disabled { opacity: 0.5; cursor: default; }
  #status { margin-top: 18px; min-height: 24px; }
  .ok { color: #6fdc8c; }
  .bad { color: #ff8a80; }
</style>
</head>
<body>
<main>
  <h1>Send a file to StageReadyVR</h1>
  <p>Upload a .txt speech script or a .pdf exported from your presentation.</p>
  <label for=""pin"">PIN shown in your headset</label>
  <input id=""pin"" inputmode=""numeric"" autocomplete=""off"" maxlength=""6"">
  <label for=""file"">File</label>
  <input id=""file"" type=""file"" accept="".txt,.pdf,text/plain,application/pdf"">
  <button id=""send"">Upload</button>
  <div id=""status"" role=""status""></div>
</main>
<script>
  var send = document.getElementById('send');
  var statusBox = document.getElementById('status');

  function show(message, good) {
    statusBox.textContent = message;
    statusBox.className = good ? 'ok' : 'bad';
  }

  send.addEventListener('click', function () {
    var pin = document.getElementById('pin').value.trim();
    var file = document.getElementById('file').files[0];
    if (!/^[0-9]{6}$/.test(pin)) { show('Enter the 6 digit PIN from your headset.', false); return; }
    if (!file) { show('Choose a file first.', false); return; }

    var isPdf = /\.pdf$/i.test(file.name);
    var isTxt = /\.txt$/i.test(file.name);
    if (!isPdf && !isTxt) { show('Only .txt and .pdf files can be imported.', false); return; }
    var limit = isPdf ? __MAX_PDF__ : __MAX_TXT__;
    if (file.size > limit) { show('The file is too large. The limit is ' + Math.floor(limit / 1048576) + ' MB for this file type.', false); return; }

    send.disabled = true;
    show('Uploading...', true);

    fetch('/upload?name=' + encodeURIComponent(file.name), {
      method: 'POST',
      headers: { 'X-Import-Pin': pin, 'Content-Type': 'application/octet-stream' },
      body: file
    })
      .then(function (r) { return r.json(); })
      .then(function (j) { show(j.message, j.ok); })
      .catch(function () { show('Could not reach the headset. Check that you are on the same Wi-Fi.', false); })
      .then(function () { send.disabled = false; });
  });
</script>
</body>
</html>";
    }
}
