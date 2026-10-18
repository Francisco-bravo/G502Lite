# G502 Lite 0.4.1

Aplicación independiente para Windows y Logitech G502 HERO / HERO K/DA USB (`046D:C08B`). Interfaz e implementación propias, basadas en la investigación del protocolo HID++. No necesita ejecutar G HUB.

## Abrir

Ejecuta `G502Lite.exe` en la carpeta `dist/G502Lite`. Conserva los archivos de esa carpeta juntos. Requiere .NET Desktop Runtime 8, para Windows x64. No requiere instalador, administrador, cuenta ni conexión a Internet.

Al abrir la ventana, únicamente consulta el dispositivo. Pulsa **Aplicar** para cambiar un ajuste. **Actualizar** vuelve a leerlo; **Reconectar** recupera la conexión tras desenchufar el mouse. Se permite una sola instancia: volver a ejecutar el programa muestra la ventana existente.

## Inicio con Windows y bandeja

La opción **Iniciar con Windows en la bandeja**, al pie de la ventana, registra el ejecutable actual para iniciar al entrar en tu sesión de Windows. Se abre únicamente su icono de ratón en la zona de iconos, sin ventana ni botón en la barra de tareas. El inicio oculto no vuelve a aplicar ajustes del mouse. Puedes desactivar esta opción desde la misma casilla o desde el menú del icono.

- Clic en el icono: abrir G502 Lite. También puedes volver a ejecutar el EXE para recuperar la misma ventana.
- Clic derecho: **Abrir G502 Lite**, **Iniciar con Windows** y **Salir**.
- El mismo menú se abre con clic derecho sobre **A la bandeja**, dentro de la ventana.
- Minimizar, cerrar con la X o pulsar **A la bandeja**: ocultar la ventana conservando el icono.
- **Salir** en el menú del icono: terminar la aplicación, esperando a que concluya una operación de guardado en curso.

El icono puede estar en el desplegable **^** de iconos ocultos de Windows. No se fuerza su posición en la barra. La casilla registra la ruta de esta copia del programa; si mueves su carpeta, vuelve a activar la casilla desde la nueva ubicación. No se necesita administrador ni se instala un servicio.

El inicio utiliza el acceso directo `G502 Lite.lnk` en la carpeta Inicio del usuario (`shell:startup`), con destino `G502Lite.exe`, argumento `--tray` y directorio de trabajo de la aplicación. Se puede consultar desde Aplicaciones de inicio del Administrador de tareas. Al activarlo se elimina la entrada antigua de G502Lite del Registro para evitar duplicados. Afecta solo al usuario actual. El inicio automático está desactivado hasta que lo actives en esta copia de la aplicación.

El diagnóstico local de arranque queda en `%LOCALAPPDATA%\G502Lite\startup.log` (con rotación a partir de 64 KiB). Registra fecha, proceso, ruta, creación del icono, salida y errores; no contiene ajustes ni datos del mouse.

## Funciones

- DPI actual: 100–25600, en pasos de 50 negociados con este mouse. Confirmación mediante lectura directa. Es un cambio inmediato; la tabla integrada original de niveles se conserva y puede volver a aplicarse al pulsar los botones de DPI o reiniciar el dispositivo.
- Frecuencia: 125, 250, 500 y 1000 Hz, según capacidades anunciadas. En modo integrado se guarda en la configuración activa del mouse; en modo software utiliza el comando directo.
- Iluminación: **Color fijo**, **Multicolor gradual** y **Apagado** para las dos zonas. En multicolor puedes elegir la duración de una vuelta completa por el espectro, de **1 a 60 segundos**, incluidos decimales con coma o punto (por ejemplo, 12,5). Un valor menor acelera la transición y uno mayor la hace más lenta. Pulsa **Aplicar iluminación** para guardarlo. El ciclo lo realiza el propio mouse y sigue funcionando con la aplicación cerrada; no se escriben colores continuamente ni se necesita un temporizador residente. La duración corresponde a la vuelta completa, no a una pausa de ese número de segundos en cada color.
- En modo integrado, el efecto y su intervalo se leen de la configuración del mouse con CRC válido, se muestran al abrir y se guardan con comprobación posterior. El campo de color fijo es una selección hexadecimal; no mide el color físico emitido.
- **Botones**: vistas superior y lateral del mouse, con puntos y etiquetas pulsables. Selecciona un botón en la imagen y elige su acción en el panel derecho: clic, multimedia, letras, F1–F12, funciones DPI o atajos comunes como copiar y pegar. Las etiquetas reflejan la asignación leída; al cambiar una opción muestran la propuesta y la marca «pendiente» hasta guardar. Puedes preparar varios cambios, alternar las vistas y pulsar **Guardar en el mouse** o **Descartar cambios**. El clic izquierdo se conserva. Se edita únicamente la configuración integrada activa; no hay biblioteca de perfiles ni vinculación a juegos. La posición física de G1–G11 se ha contrastado con los metadatos del modelo G502; no se detectan pulsaciones físicas para seleccionarlos.
- Copia completa de los 16 sectores de usuario, exportación y restauración de la configuración activa.

Para guardar botones, frecuencia o luces en memoria, el mouse debe estar en **Memoria integrada** y G HUB cerrado. La aplicación no cambia el modo operativo automáticamente. Las asignaciones guardadas funcionan con G502 Lite cerrado.

Cada escritura en memoria crea primero un JSON completo en `backups`, junto al ejecutable. Se valida CRC, se comprueba que la memoria no haya cambiado desde su lectura, se conservan los bytes desconocidos y se compara la memoria después del guardado. En **Botones → Restaurar copia…** puedes seleccionar una copia de esa carpeta; restaura solamente la configuración activa correspondiente. No desconectes el cable durante el breve guardado.

## Alcance de esta versión

No incluye juegos, comunidad, login, cuentas, nube, telemetría, publicidad, actualizador ni servicios de Windows. El inicio automático en la bandeja es opcional. No abre conexiones de red ni inicia componentes de Logitech.

No incluye editor de macros de varias acciones, ejecución Lua, importación de macros de G HUB, editor de G-Shift ni cambio de la tabla integrada de niveles/Shift. Los atajos de teclado son asignaciones HID de una tecla con modificadores, no secuencias de macros. Las macros y la capa G-Shift existentes se conservan al editar otros campos. No se han probado otros modelos/PID ni el modo software de G HUB con este ejecutable; las pruebas reales se hicieron en modo integrado.

Los cambios RGB se verificaron en la memoria y mediante la aceptación de la recarga; no se midió el color de los LED con un instrumento. La frecuencia leída es el ajuste del dispositivo, no una medición del tráfico USB. Si Windows u otra aplicación controla las luces, puede volver a imponer su efecto.

## Compilar y comprobar

Desde la raíz del repositorio con SDK .NET 8 o compatible:

```powershell
dotnet build G502Lite/G502Lite.csproj -c Release
dotnet run --project G502Lite.Tests/G502Lite.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File G502Lite/publish.ps1
```

Los 41 tests automáticos cubren integridad de memoria/protocolo, correspondencia de botones, iluminación y validación del comando de inicio. Usan datos sintéticos; no requieren mouse, copias de memoria ni archivos privados y no modifican el Registro. Las pruebas con hardware se realizaron en un G502 HERO K/DA en modo integrado. El repositorio no incluye volcados del dispositivo, registros locales ni binarios de G HUB.

Diagnóstico y copia de solo lectura (esperar al proceso al usar PowerShell):

```powershell
.\G502Lite.exe --diagnose diagnostico.json | Out-Null
.\G502Lite.exe --backup copia.json | Out-Null
.\G502Lite.exe --startup-status inicio.json | Out-Null
```

`--self-test`, `--test-buttons`, `--test-lighting` y `--test-cycle` son pruebas de desarrollo: cambian temporalmente ajustes reales y después intentan restaurarlos. No son necesarias para el uso normal.

`--tray` abre únicamente el icono de bandeja. `--startup-enable resultado.json` y `--startup-disable resultado.json` permiten activar/desactivar el inicio automático de la misma forma que la casilla; devuelven el estado en el JSON indicado.

Referencias del protocolo: [Logitech HID++](https://github.com/Logitech/cpg-docs/tree/master/hidpp20), [libratbag](https://github.com/libratbag/libratbag) y [Solaar](https://github.com/pwr-Solaar/Solaar), consultadas para interoperabilidad. No se incorpora su código. El ejecutable no contiene ni carga código o binarios de LGHUB. Las dos imágenes del mouse son versiones editadas con la herramienta integrada de imágenes a partir de las capturas proporcionadas por el usuario: se quitaron textos, líneas, marcadores y logos; la aplicación dibuja encima sus propios controles. Están integradas en el ejecutable administrado como recursos de `Assets`.
