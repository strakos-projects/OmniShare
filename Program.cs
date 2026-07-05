using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OmniShare
{
    class Program
    {
        private static readonly string StorageDirectory = @"C:\OmniStorage";
        private static readonly string ConfigFile = "config.ini";
        private static readonly string HostUrl = "http://+:8080/";
        private static string AdminUsername = "admin";
        private static string AdminPasswordHash = "";

        static void Main(string[] args)
        {
            Console.Title = "OmniShare - HTTP Node [Windows XP]";

            EnsureStorageExists();
            LoadOrSetupCredentials();

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(HostUrl);
            // Autentizaci si budeme řešit manuálně, abychom nebyli závislí na Windows účtech
            listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;

            try
            {
                listener.Start();
                Console.WriteLine(String.Format("[+] OmniShare is running on {0}", HostUrl));
                Console.WriteLine("[!] Press Ctrl+C to exit\n");

                while (true)
                {
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(ProcessRequest, context);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("[!] Fatal Error: {0}", ex.Message));
            }
        }

        #region Setup & Security
        private static void EnsureStorageExists()
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
                Console.WriteLine(String.Format("[+] Created storage dir: {0}", StorageDirectory));
            }
        }

        private static void LoadOrSetupCredentials()
        {
            if (File.Exists(ConfigFile))
            {
                string[] lines = File.ReadAllLines(ConfigFile);
                foreach (string line in lines)
                {
                    if (line.StartsWith("PasswordHash="))
                        AdminPasswordHash = line.Substring(13).Trim();
                }
                Console.WriteLine("[*] Credentials loaded.");
            }
            else
            {
                Console.WriteLine("========================================");
                Console.WriteLine(" FIRST RUN SETUP");
                Console.WriteLine("========================================");
                Console.Write(" Set password for user 'admin': ");
                string plainPassword = Console.ReadLine();

                AdminPasswordHash = ComputeSha256Hash(plainPassword);
                File.WriteAllText(ConfigFile, String.Format("Username={0}\r\nPasswordHash={1}", AdminUsername, AdminPasswordHash));

                Console.WriteLine("[+] Config saved. Please keep config.ini safe.");
                Console.WriteLine("========================================\n");
            }
        }

        private static string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            string authHeader = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
                return false;

            string encodedStr = authHeader.Substring("Basic ".Length).Trim();
            string decodedStr = Encoding.UTF8.GetString(Convert.FromBase64String(encodedStr));
            string[] parts = decodedStr.Split(new char[] { ':' }, 2);

            if (parts.Length == 2)
            {
                string reqUser = parts[0];
                string reqPassHash = ComputeSha256Hash(parts[1]);
                return (reqUser == AdminUsername && reqPassHash == AdminPasswordHash);
            }
            return false;
        }
        #endregion

        #region HTTP Handler
        private static void ProcessRequest(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            Console.WriteLine(String.Format("[{0:HH:mm:ss}] {1} {2}", DateTime.Now, request.HttpMethod, request.Url.AbsolutePath));

            try
            {
                // Kontrola Basic Authentication
                if (!IsAuthorized(request))
                {
                    response.StatusCode = 401;
                    response.AddHeader("WWW-Authenticate", "Basic realm=\"OmniShare Secure Node\"");
                    WriteTextResponse(response, "401 Unauthorized");
                    return;
                }

                string path = request.Url.AbsolutePath.ToLower();

                // 1. Zobrazení UI
                if (request.HttpMethod == "GET" && path == "/")
                {
                    ServeHtmlIndex(response);
                }
                // 2. API: Seznam souborů
                else if (request.HttpMethod == "GET" && path == "/api/files")
                {
                    HandleListFiles(response);
                }
                // 3. API: Stažení/zobrazení souboru
                else if (request.HttpMethod == "GET" && path.StartsWith("/f/"))
                {
                    HandleFileDownload(request, response);
                }
                // 4. API: Upload souboru (Raw Body)
                else if (request.HttpMethod == "POST" && path == "/api/upload")
                {
                    HandleFileUpload(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    WriteTextResponse(response, "Not Found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("[!] Error: {0}", ex.Message));
                response.StatusCode = 500;
            }
            finally
            {
                response.Close();
            }
        }
        #endregion

        #region Endpoints Logic
        private static void HandleListFiles(HttpListenerResponse response)
        {
            string[] files = Directory.GetFiles(StorageDirectory);
            StringBuilder json = new StringBuilder("[");

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                json.AppendFormat("\"{0}\"", fileName);
                if (i < files.Length - 1) json.Append(",");
            }
            json.Append("]");

            byte[] buffer = Encoding.UTF8.GetBytes(json.ToString());
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void HandleFileDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            string rawFileName = request.Url.AbsolutePath.Substring(3); // odstraníme "/f/"
            string safeFileName = Path.GetFileName(Uri.UnescapeDataString(rawFileName)); // Ochrana proti Path Traversal
            string fullPath = Path.Combine(StorageDirectory, safeFileName);

            if (File.Exists(fullPath))
            {
                // Pokusíme se uhodnout MIME type podle přípony
                string ext = Path.GetExtension(safeFileName).ToLower();
                if (ext == ".txt" || ext == ".log") response.ContentType = "text/plain; charset=utf-8";
                else if (ext == ".png") response.ContentType = "image/png";
                else if (ext == ".jpg" || ext == ".jpeg") response.ContentType = "image/jpeg";
                else response.ContentType = "application/octet-stream"; // Pro stažení

                // Přímé nastavení hlavičky, aby prohlížeč neukládal agresivně do cache
                response.AddHeader("Cache-Control", "no-cache");

                byte[] fileBytes = File.ReadAllBytes(fullPath);
                response.ContentLength64 = fileBytes.Length;
                response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
            }
            else
            {
                response.StatusCode = 404;
            }
        }

        private static void HandleFileUpload(HttpListenerRequest request, HttpListenerResponse response)
        {
            string rawName = request.QueryString["name"];
            if (string.IsNullOrEmpty(rawName))
            {
                response.StatusCode = 400;
                return;
            }

            string safeFileName = Path.GetFileName(Uri.UnescapeDataString(rawName));
            string fullPath = Path.Combine(StorageDirectory, safeFileName);

            using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                request.InputStream.CopyTo(fs);
            }

            WriteTextResponse(response, "OK");
        }
        #endregion

        #region Helpers
        private static void WriteTextResponse(HttpListenerResponse response, string text)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void ServeHtmlIndex(HttpListenerResponse response)
        {
            // Minimalistický, plně responzivní UI (HTML5 + Vanilla JS) v angličtině
            string html = @"<!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='utf-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <title>OmniShare Storage</title>
                <style>
                    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f4f9; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }
                    .header { background: #2c3e50; color: white; padding: 15px 20px; border-radius: 8px; margin-bottom: 20px; display: flex; justify-content: space-between; align-items: center; }
                    .header h1 { margin: 0; font-size: 1.5rem; }
                    .upload-box { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); margin-bottom: 20px; }
                    input[type='file'] { font-size: 1rem; margin-bottom: 10px; display: block; }
                    button { background: #3498db; color: white; border: none; padding: 10px 20px; font-size: 1rem; border-radius: 5px; cursor: pointer; }
                    button:hover { background: #2980b9; }
                    button:disabled { background: #bdc3c7; cursor: not-allowed; }
                    .file-list { background: white; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); padding: 20px; }
                    .file-item { display: flex; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eee; align-items: center; }
                    .file-item:last-child { border-bottom: none; }
                    .file-links a { margin-left: 10px; color: #3498db; text-decoration: none; font-weight: bold; }
                    .file-links a:hover { text-decoration: underline; }
                </style>
            </head>
            <body>
                <div class='header'>
                    <h1>📦 OmniShare</h1>
                    <span>Secure Node</span>
                </div>

                <div class='upload-box'>
                    <h3>Upload New File</h3>
                    <input type='file' id='fileInput'>
                    <button id='uploadBtn' onclick='uploadFile()'>Upload</button>
                    <span id='status' style='margin-left: 10px; color: green;'></span>
                </div>

                <div class='file-list'>
                    <h3>Stored Files</h3>
                    <div id='filesContainer'>Loading...</div>
                </div>

                <script>
                    function loadFiles() {
                        fetch('/api/files')
                            .then(res => res.json())
                            .then(files => {
                                const container = document.getElementById('filesContainer');
                                container.innerHTML = '';
                                if(files.length === 0) {
                                    container.innerHTML = '<i>No files found.</i>';
                                    return;
                                }
                                files.forEach(file => {
                                    const div = document.createElement('div');
                                    div.className = 'file-item';
                                    
                                    const nameSpan = document.createElement('span');
                                    nameSpan.textContent = file;
                                    
                                    const links = document.createElement('div');
                                    links.className = 'file-links';
                                    
                                    const downloadLink = document.createElement('a');
                                    downloadLink.href = '/f/' + encodeURIComponent(file);
                                    downloadLink.textContent = 'Open / Download';
                                    downloadLink.target = '_blank';
                                    
                                    links.appendChild(downloadLink);
                                    div.appendChild(nameSpan);
                                    div.appendChild(links);
                                    container.appendChild(div);
                                });
                            });
                    }

                    function uploadFile() {
                        const input = document.getElementById('fileInput');
                        const btn = document.getElementById('uploadBtn');
                        const status = document.getElementById('status');
                        
                        if (input.files.length === 0) {
                            alert('Please select a file first.');
                            return;
                        }

                        const file = input.files[0];
                        btn.disabled = true;
                        status.textContent = 'Uploading...';

                        // Upload via raw binary body (Fetch API)
                        fetch('/api/upload?name=' + encodeURIComponent(file.name), {
                            method: 'POST',
                            body: file
                        }).then(res => {
                            if(res.ok) {
                                status.textContent = 'Success!';
                                input.value = '';
                                loadFiles();
                            } else {
                                status.textContent = 'Error uploading.';
                            }
                        }).catch(() => {
                            status.textContent = 'Network error.';
                        }).finally(() => {
                            btn.disabled = false;
                            setTimeout(() => status.textContent = '', 3000);
                        });
                    }

                    // Initial load
                    loadFiles();
                </script>
            </body>
            </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        #endregion
    }
}