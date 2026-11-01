# Changelog — sOC Scanner

## 2026.11.1.1 — Microsoft Store

**Español**

- **Microsoft Store**: paquete MSIX (`tools\empaquetar-msix.ps1`, declara la cámara) con imágenes y
  fichas en `store/microsoft` (`tools\generar-icono.ps1` y `tools\capturas-store.py`).

**English**

- **Microsoft Store**: MSIX package (`tools\empaquetar-msix.ps1`, declares the webcam) with images and
  listings in `store/microsoft` (`tools\generar-icono.ps1` and `tools\capturas-store.py`).

## 2026.10.22.0 — Guardar lo escaneado con escáneres de verdad · Saving scans from real scanners

**Español**

- **Arreglo: no se podía guardar** (PDF, ni en serie ni una a una) lo escaneado con una impresora multifunción por USB:
  salía «The calling thread cannot access this object because a different thread owns it». La hoja
  llegaba de un fichero y se quedaba atada al hilo que la leyó; ahora se copia al leerla y se guarda
  sin problema en cualquier formato.
- Las opciones que el escáner no admite ya no se le piden (brillo, contraste, zona y resolución se
  ajustan a sus límites; el recorte automático y la doble cara solo si los tiene) y el registro dice
  cuál ha rechazado, en vez de solo «El parámetro no es correcto».

**English**

- **Fix: scans could not be saved** (PDF, in series or one by one) with a multifunction printer over USB: «The
  calling thread cannot access this object because a different thread owns it». The page came from a
  file and stayed tied to the thread that read it; now it is copied when read and saves in any format.
- Options the scanner does not support are no longer requested (brightness, contrast, area and
  resolution are kept within its limits; auto-cropping and duplex only if it has them), and the log says
  which one it rejected instead of just «The parameter is incorrect».

## 2026.10.21.0 — Primera versión · First version

**Español**

- **Escanear con un solo botón** (o Intro) con cualquier escáner que reconozca Windows —por USB o por
  red, del cristal o del alimentador— o con una cámara, con vista en directo.
- **Calidades a un clic** (texto en blanco y negro, documento en gris, documento en color, foto) o a
  medida: resolución, color, tamaño de la hoja, brillo, contraste y calidad JPEG, ajustadas a lo que
  admite cada dispositivo y diciéndolo cuando no llega.
- **Una a una**: vista previa, girar, ordenar y quitar hojas, y guardar en PDF (todas las hojas en un
  fichero), TIFF, JPG, PNG o BMP.
- **En serie**: cada hoja se guarda sola nada más escanearla, en la carpeta y el formato fijados, con
  prefijo, número correlativo y sufijo; nunca se pisa un fichero.
- PDF escrito por la propia aplicación: una página por hoja del tamaño real; el blanco y negro, en 1
  bit sin pérdida.
- Cobertura de toda la aplicación: 90,5 % (113 pruebas, 2 de ellas con hardware real) y 9 pruebas de
  interfaz.

**English**

- **Scan with a single button** (or Enter) with any scanner Windows recognises —USB or network, glass
  or feeder— or with a camera, with a live view.
- **One-click qualities** (black-and-white text, grey document, colour document, photo) or your own:
  resolution, colour, page size, brightness, contrast and JPEG quality, adjusted to what each device
  can do and saying so when it cannot.
- **One at a time**: preview, rotate, reorder and remove pages, and save as PDF (all pages in one
  file), TIFF, JPG, PNG or BMP.
- **Batch**: each sheet is saved on its own as soon as it is scanned, to the folder and format you set,
  with a prefix, a sequential number and a suffix; an existing file is never overwritten.
- PDF written by the app itself: one page per sheet at its real size; black and white in lossless 1 bit.
- Whole-app coverage: 90.5 % (113 tests, 2 of them with real hardware) and 9 UI tests.
