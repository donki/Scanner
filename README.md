# sOC Scanner (sOCScanner)

Escanea **con un solo botón** con cualquier escáner que reconozca Windows o con una cámara, hoja a
hoja con vista previa o **en serie** (cada hoja se guarda sola con nombre correlativo), a **PDF, JPG,
PNG, TIFF o BMP**.

## Dónde conseguirla

- **GitHub**: el exe autocontenido de cada versión en
  [Releases](https://github.com/donki/Scanner/releases) (sin instalar: doble clic).
- **Microsoft Store**: aún no.

## Qué hace

- **Cualquier escáner o cámara.** Usa la API de escáneres de Windows (`Windows.Devices.Scanners`),
  que cubre WIA, WSD y eSCL: escáneres e impresoras multifunción **por USB o por red**, del **cristal**
  o del **alimentador** (todas las hojas de una vez). Una misma impresora puede salir dos veces (por
  USB y por red): por USB suele dar más resolución. También vale **una cámara** (la del portátil, una
  webcam o una cámara de documentos): vista en directo, resolución a elegir y, al pulsar, se queda la
  imagen.
- **Calidad a un clic**: texto en blanco y negro (300 ppp, lo más ligero y nítido para letra),
  documento en gris (200 ppp), documento en color (200 ppp) o foto (600 ppp). **A medida**: resolución
  (solo las que admite el dispositivo elegido), color, gris o blanco y negro, tamaño (A4, Carta, A5 o
  todo el cristal), brillo, contraste y calidad JPEG, con los megapíxeles y los MB de una hoja a la
  vista. Lo que el dispositivo no admite se ajusta solo y se dice («pediste 600; este dispositivo no
  llega»): el blanco y negro y el gris se sacan después de una imagen en color si hace falta, y el
  brillo y el contraste se aplican a la imagen si el dispositivo no los tiene.
- **Una a una**: pones la hoja, pulsas el botón (o **Intro**) y sale en la vista previa; puedes
  escanear más, **girar, ordenar o quitar** hojas y **guardar** en PDF (todas las hojas en un fichero),
  TIFF (también varias en uno), JPG, PNG o BMP (una por fichero).
- **En serie**: cada hoja se guarda **sola nada más escanearla**, en la carpeta y el formato fijados,
  con **prefijo + número correlativo con ceros + sufijo** (`Factura_0007.pdf`). Se ve antes el nombre
  de la próxima. Nunca se pisa un fichero: si el número ya existe se salta al siguiente libre. El
  contador se guarda para seguir otro día.
- **El PDF lo escribe la propia aplicación** (sin bibliotecas): una página por hoja, del tamaño real
  de la hoja; el blanco y negro va en 1 bit sin pérdida (una hoja de texto ocupa unas decenas de KB) y
  el color y el gris en JPEG.
- **Al terminar**: abrir, enseñar en su carpeta o copiar el fichero (se pega tal cual en un correo o
  un chat). Esc cancela un escaneo.
- **Los fallos se explican** (sin papel, atasco, tapa abierta, ocupado, calentando —se reintenta
  solo—, apagado o desconectado, permiso de la cámara, fichero abierto en otro programa, disco lleno…)
  con la razón y qué hacer.
- Guía de configuración (dispositivo en Windows, permiso de la cámara, calidad, carpeta de la serie),
  novedades de cada versión, español e inglés, tema claro y oscuro, instancia única.

## Cómo se ejecuta

```
dotnet build Scanner.slnx
dotnet run --project src\Scanner.App
```

Necesita Windows 10 (19041) o posterior y el SDK de .NET 10. El exe publicado es autocontenido: no
hace falta instalar nada (`tools\entregar.ps1 -Version <v> -SoloCompilar` lo deja en `publish\app`).

**Modo aislado** (solo Debug, General §8.4): con `SOC_SANDBOX=<carpeta>` los ajustes, el registro y
los escaneos van a esa carpeta, la lista solo tiene **un escáner y una cámara simulados** (no se toca
ningún dispositivo de verdad) y las ventanas no se activan al abrirse.

## A qué accede y qué puede romper

- **Escáneres y cámaras**, solo al elegirlos y escanear. La cámara se enciende mientras está elegida
  en la ventana.
- **Escribe** solo en la carpeta que elijas (en serie nunca sobrescribe; al guardar a mano, Windows
  pregunta antes de sobrescribir) y, mientras escanea, en una carpeta temporal que borra al acabar.
- Sus ajustes en `%AppData%\sOCScanner\settings.json` y un registro de errores en
  `%LocalAppData%\sOCScanner\errors.log` (1 MB como mucho, sin datos personales).
- **No se conecta a internet** ni sube nada.

## Estructura

- `src/Scanner.Core`: la lógica (calidades y su ajuste a cada dispositivo, nombres, PDF propio,
  formatos, tratamiento de imagen, documento, flujo de escanear, escáner y cámara simulados, ajustes,
  textos).
- `src/Scanner.Engine`: el hardware con las API de Windows (escáneres WinRT y cámaras con
  `MediaCapture` y lector de fotogramas).
- `src/Scanner.App`: la interfaz WPF.

## Pruebas

`tests/Scanner.Tests` (xUnit): **113 pruebas**, todas en verde. Prueban la lógica y también **las
ventanas** (la principal con el escáner y la cámara simulados, calidad, guía, novedades, «Acerca de»,
avisos), en un hilo STA propio, fuera de la pantalla y con los diálogos de Windows contestados por la
prueba (`Ui.cs`). Dos de ellas son de integración y solo corren con `SOC_SCAN_INTEGRATION=1`:
**escanean de verdad** con cada escáner que vea Windows (a 100 ppp, sin guardar nada) y toman una
imagen con la primera cámara. Fecha: 2026-10-21.

- Cobertura de **toda la aplicación** (Core + Engine + interfaz, todo instrumentado):
  **90,5 %** (2997 de 3308 líneas). Lógica (`Scanner.Core`): 98,7 %.
- Lo que queda sin cubrir es sobre todo el arranque real (instancia única, `OnStartup`) y los caminos
  de error del hardware que no se pueden provocar sin un dispositivo que falle.
- Tiempo del banco: **≈ 6 s** (sin integración); **≈ 27 s** con las dos de integración.

`tests/Scanner.UITests` (FlaUI, modo aislado `SOC_SANDBOX`): **9 pruebas** de interfaz en **≈ 55 s**
sobre el exe Debug (ver su [README](tests/Scanner.UITests/README.md)).

```
dotnet test tests\Scanner.Tests
$env:SOC_SCAN_INTEGRATION=1; dotnet test tests\Scanner.Tests --collect:"XPlat Code Coverage"
dotnet tool restore
dotnet tool run reportgenerator -reports:tests\Scanner.Tests\TestResults\*\coverage.cobertura.xml -targetdir:cobertura -reporttypes:TextSummary
dotnet test tests\Scanner.UITests
```

## Licencia

MIT (`LICENSE`). Dependencias y su licencia en `THIRD-PARTY-NOTICES.md`; privacidad en `PRIVACY.md`.
