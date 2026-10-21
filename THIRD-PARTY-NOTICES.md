# Avisos de terceros — sOC Scanner

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| .NET 10 (runtime, WPF, WinRT/CsWinRT) | Plataforma de la aplicación. Va dentro del exe autocontenido. | MIT | .NET Foundation y colaboradores |
| API de Windows (Windows.Devices.Scanners, Windows.Media.Capture, WIC, user32, dwmapi) | Escáneres (WIA, WSD, eSCL), cámaras, lectura y escritura de imágenes, ventanas. | API del sistema operativo | Microsoft |
| Segoe Fluent Icons / Segoe MDL2 Assets | Iconos planos de los botones. Fuentes del propio Windows; no se distribuyen. | Fuente del sistema operativo | Microsoft |

Solo para las pruebas (no van en el exe): xUnit (Apache 2.0), Microsoft.NET.Test.Sdk (MIT), coverlet
(MIT), ReportGenerator (Apache 2.0), FlaUI (MIT).

El PDF lo escribe código propio (`src/Scanner.Core/Output/PdfWriter.cs`): no se usa ninguna
biblioteca de PDF. Todo el código (`*.cs`, `*.xaml`, scripts) es propio y va bajo MIT (`LICENSE`).
