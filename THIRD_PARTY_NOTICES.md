> GlassBoard itself is distributed under GPL-3.0 (see LICENSE). The components below keep their own licenses.

# Third-party components

Interop.UIAutomationClient.dll: Microsoft UI Automation interop assembly, bundled for the in-app self-check and UI reading. No upstream source is bundled.

ManagedShell 0.0.374 (https://github.com/cairoshell/ManagedShell), Copyright (c) the ManagedShell contributors (cairoshell), licensed under the Apache License 2.0 (http://www.apache.org/licenses/LICENSE-2.0). Used unmodified for the tray bar. Its assemblies are embedded in GlassBoard.exe and extracted to %LOCALAPPDATA%\GlassBoard\runtime at run time.

ManagedShell dependencies embedded the same way, from Microsoft under the MIT License (https://github.com/dotnet/runtime/blob/main/LICENSE.TXT): System.Data.OleDb, System.Diagnostics.EventLog, System.Security.Principal.Windows, System.ServiceProcess.ServiceController.

Other reviewed projects (no source copied):
https://github.com/DubyaDude/WindowsMediaController
https://github.com/ModernFlyouts-Community/ModernFlyouts

Weather backup in v0.9.9: https://wttr.in/ (https://github.com/chubin/wttr.in). Requested only when the Open-Meteo lookup/forecast fails. Requests contain the entered city or resolved coordinates; no account integration. Actual returned provider is displayed. No wttr.in source code is bundled.
