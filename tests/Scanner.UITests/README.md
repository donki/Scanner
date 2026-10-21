# Pruebas de interfaz — sOC Scanner

FlaUI (UIA3, MIT) sobre el exe **Debug** ya compilado, en modo aislado (`SOC_SANDBOX` con una carpeta
nueva por prueba): ajustes y escaneos en esa carpeta, solo el **escáner y la cámara simulados** (no
se toca ningún dispositivo de verdad) y ventanas que no se activan al abrirse. Todo va por patrones de
UI Automation (Invoke, Value, SelectionItem), sin ratón ni teclado, y nunca se busca por el texto.

```
dotnet build Scanner.slnx
dotnet test tests\Scanner.UITests
```

`SCANNER_EXE` cambia el exe que se prueba (tiene que ser Debug: en Release no hay modo aislado).

| Prueba | Qué recorre |
|---|---|
| Arranca en modo aislado | título con `[SOC_SANDBOX]`, escáner simulado, calidad por defecto |
| Una a una | escanear dos hojas, ordenar, girar, quitar y guardar en PDF por el diálogo de Windows |
| Descartar | la confirmación (cancelar y aceptar) |
| En serie | prefijo y sufijo, dos hojas guardadas solas con su número, contador guardado |
| Prefijo prohibido | aviso, vuelve a lo guardado y no se escanea |
| Calidad a medida | blanco y negro a 300 ppp, queda «Personalizada» y la hoja sale así |
| Idioma | cambia al momento sin perder las hojas |
| Acerca de, novedades y guía | se abren, se recorren y se cierran |
| Primera vez | sale la guía sola |

Cada paso deja una captura en `artifacts/` (fuera de git, hecha con `PrintWindow`, sin robar el
foco). **Ojo**: la prueba de guardar usa el diálogo de guardar de Windows, que sí se activa: no
lanzar la tanda mientras se escribe en otra ventana.
