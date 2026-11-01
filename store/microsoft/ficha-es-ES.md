# Descripción de Store — Español (España)

Todo lo de aquí es para pegar tal cual en el formulario de Partner Center. Paquete:
`tools\empaquetar-msix.ps1` → `bin\sOCScanner.msix` (identidad de *Product management › Product identity*).

## Nombre del producto

```
sOC Scanner
```

## Descripción

```
sOC Scanner escanea con un solo botón (o con Intro) con cualquier escáner que reconozca Windows o con una cámara, hoja a hoja o en serie, a PDF, JPG, PNG, TIFF o BMP.

CUALQUIER ESCÁNER O CÁMARA
• Escáneres e impresoras multifunción por USB o por red, del cristal o del alimentador.
• También una cámara: la del portátil, una webcam o una cámara de documentos, con vista en directo.

CALIDAD A UN CLIC
• Texto en blanco y negro, documento en gris, documento en color o foto; o a medida: resolución, color, tamaño de hoja, brillo, contraste y calidad JPEG, con el tamaño de cada hoja a la vista.
• Lo que el dispositivo no admite se ajusta solo y se explica.

UNA A UNA O EN SERIE
• Una a una: escanea, gira, ordena o quita hojas y guárdalas en un PDF o TIFF de varias páginas, o en JPG, PNG o BMP.
• En serie: cada hoja se guarda sola nada más escanearla con un nombre correlativo (Factura_0007.pdf). Nunca pisa un fichero.
• Al terminar: abrir, enseñar en su carpeta o copiar el fichero para pegarlo en un correo o un chat.

Los fallos se explican (sin papel, atasco, tapa abierta, permiso de la cámara, disco lleno…) con qué hacer. No se conecta a internet ni sube nada. Sin cuenta, sin anuncios, sin rastreadores. Software libre bajo licencia MIT. En español y en inglés, con modo claro y oscuro.
```

## Novedades de esta versión

En blanco en el primer envío.

## Características del producto

```
Escanea con un botón o con Intro
Cualquier escáner de Windows, por USB o por red, o una cámara
Una a una o en serie, con nombres correlativos
PDF y TIFF de varias páginas, JPG, PNG y BMP
Calidad a un clic o a medida
Sin internet, sin cuenta, sin anuncios
```

## Palabras clave

```
escáner, escanear, pdf, documentos, digitalizar, cámara, tiff, multifunción
```

## Categoría

Productividad.

## Capturas

`capturas/01.png`–`03.png` (1600×900, escáner simulado del modo aislado), hechas con
`python tools\capturas-store.py <png>:32 …` a partir de las de las pruebas de interfaz (el 32 quita
la barra de título con `[SOC_SANDBOX]`). Nunca la de la serie: enseña la ruta con el usuario.

## Logotipos

`logos/` (icono 300/150/71, caja 1:1, póster 9:16 y héroe 16:9), con `tools\generar-icono.ps1`.

## Dependencias de software (política 10.2.4.1)

Ninguna: usa la API de escáneres y cámaras de Windows.

## Privacidad

URL: https://socraticweb0.wordpress.com/privacidad/ — sOC Scanner no recoge ningún dato; la cámara
solo se enciende mientras está elegida en la ventana y lo escaneado se queda en el PC.
