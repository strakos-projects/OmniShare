# 📦 OmniShare

A lightweight, self-contained HTTP web storage node engineered specifically to run natively on legacy Windows XP hardware. Built with .NET Framework 4.0 and a zero-dependency HTML5/JS web UI.

## ✨ Features

- **Legacy Support:** Fully compatible with Windows XP SP3 (32-bit).
- **Standalone Server:** Uses native `HttpListener` — no IIS or heavy web servers required.
- **Modern Frontend:** Responsive Vanilla JS and HTML5 interface served directly from the C# backend.
- **Security First:**
  - HTTP Basic Authentication.
  - Passwords are locally hashed (SHA-256) and never stored in plain text.
  - Built-in protection against Path Traversal attacks.
- **Efficient Uploads:** Utilizes raw binary body streams via the Fetch API for memory-efficient file uploads.

## 🛠️ Tech Stack

- **Runtime:** .NET Framework 4.0
- **Language:** C# 4.0
- **Frontend:** HTML5, CSS3, Vanilla JavaScript

## 🚀 Quick Start

1. **Build the project:**
   Compile the project in Visual Studio. Ensure the target is set to `Release`, framework to `net40`, and architecture explicitly to `win-x86`.

2. **Deploy to Windows XP:**
   Copy the compiled `OmniShare.exe` to your target machine. (Note: The machine must have .NET Framework 4.0 installed).

3. **Run as Administrator:**
   The application must be executed with Administrator privileges to allow `HttpListener` to bind to all network interfaces (`http://+:8080/`).

4. **First-Run Setup:**
   On the first launch, the console will prompt you to set an admin password. A `config.ini` file will be generated containing the SHA-256 hash of your password.

5. **Access the UI:**
   Open a web browser on any device in the same network and navigate to:
   `http://<YOUR-XP-MACHINE-IP>:8080/`
   _(Login using the username `admin` and your newly created password)._
