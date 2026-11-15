# G502 Lite 0.4.3

Aplicación independiente para Windows y Logitech G502 HERO / HERO K/DA USB (`046D:C08B`). Interfaz e implementación propias, basadas en la investigación del protocolo HID++. No necesita ejecutar G HUB.

## Abrir

Ejecuta `G502Lite.exe` en la carpeta `dist/G502Lite`. Conserva los archivos de esa carpeta juntos. Requiere .NET Desktop Runtime 8, para Windows x64. No requiere instalador, administrador, cuenta ni conexión a Internet.

Al abrir la ventana, únicamente consulta el dispositivo. Pulsa **Aplicar** para cambiar un ajuste. **Actualizar** vuelve a leerlo; **Reconectar** recupera la conexión tras desenchufar el mouse. Se permite una sola instancia: volver a ejecutar el programa muestra la ventana existente.

## Inicio con Windows y bandeja

La opción **Iniciar con Windows en la bandeja**, al pie de la ventana, registra el ejecutable actual para iniciar al entrar en tu sesión de Windows. Se abre únicamente su icono de ratón en la zona de iconos, sin ventana ni botón en la barra de tareas. El inicio oculto consulta el mouse sin reescribir ajustes. La aplicación vigila la conexión también en la bandeja. Puedes desactivar esta opción desde la misma casilla o desde el menú del icono.

- Clic en el icono: abrir G502 Lite. También puedes volver a ejecutar el EXE para recuperar la misma ventana.
- Clic derecho: **Abrir G502 Lite**, **Iniciar con Windows** y **Salir**.
- El mismo menú se abre con clic derecho sobre **A la bandeja**, dentro de la ventana.
- Minimizar, cerrar con la X o pulsar **A la bandeja**: ocultar la ventana conservando el icono.
- **Salir** en el menú del icono: terminar la aplicación, esperando a que concluya una operación de guardado en curso.

El icono puede estar en el desplegable **^** de iconos ocultos de Windows. No se fuerza su posición en la barra. La casilla registra la ruta de esta copia del programa; si mueves su carpeta, vuelve a activar la casilla desde la nueva ubicación. No se necesita administrador ni se instala un servicio.

El inicio utiliza el acceso directo `G502 Lite.lnk` en la carpeta Inicio del usuario (`shell:startup`), con destino `G502Lite.exe`, argumento `--tray` y directorio de trabajo de la aplicación. Se puede consultar desde Aplicaciones de inicio del Administrador de tareas. Al activarlo se elimina la entrada antigua de G502Lite del Registro para evitar duplicados. Afecta solo al usuario actual. El inicio automático está desactivado hasta que lo actives en esta copia de la aplicación.

El diagnóstico local de arranque queda en `%LOCALAPPDATA%\G502Lite\startup.log` (con rotación a partir de 64 KiB). Registra fecha, proceso, ruta, creación del icono, salida y errores; no contiene ajustes ni datos del mouse.

## Recuperación tras desconexión USB

Si el hub USB o el mouse se desconectan, G502 Lite descarta la conexión HID anterior y vuelve a buscar el dispositivo cada dos segundos. Tras recuperar una conexión previamente establecida, si el mouse está en memoria integrada y G HUB no está activo, recarga la misma configuración activa para recuperar las asignaciones. No cambia de configuración ni de modo, no reescribe sectores y no repite guardados interrumpidos. Comprueba el CRC, conserva el DPI actual y compara la memoria antes y después. Si falla la validación, muestra el error.

También puedes pulsar **Recuperar botones** para recargar manualmente la configuración activa. Si guardaste cambios en un editor abierto durante la desconexión, ciérralo para permitir recuperar la conexión y vuelve a leer la memoria antes de guardar.
## Indicador de pulsaciones

La tarjeta **Prueba de botones** muestra la última acción recibida del G502, su pulsación/liberación, la hora y un contador. En **Botones**, los puntos y etiquetas de la imagen destellan en verde durante unos instantes cuando la acción recibida coincide con una asignación guardada. Se utiliza la configuración guardada, no una propuesta pendiente de guardar. Si varios botones comparten la misma acción, se muestran los candidatos sin afirmar cuál se pulsó.

Detecta clics, rueda, controles multimedia y los atajos de teclado admitidos enviados por el propio G502. Solo procesa eventos de dispositivos con VID/PID 046D:C08B. El indicador se actualiza con la ventana principal o el editor visibles; si hay una acción de clics automáticos asignada, el procesamiento permanece activo en la bandeja; no recoge las teclas de otros teclados ni guarda un historial en disco. El contador se reinicia al cerrar el programa. El indicador no bloquea entradas: las aplicaciones siguen recibiendo las pulsaciones normalmente. La asignación opcional de triple clic sí genera clics adicionales.

Los controles internos como DPI, G-Shift o cambio de memoria no siempre producen eventos de entrada en Windows y no se presentan como pulsaciones detectadas. En modo software, las acciones recibidas pueden diferir de la configuración integrada. La tarjeta confirma que llegó una entrada, no que YouTube u otro reproductor la haya ejecutado.
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

Los 60 tests automáticos cubren integridad de memoria/protocolo, correspondencia de botones, iluminación y validación del comando de inicio. Usan datos sintéticos; no requieren mouse, copias de memoria ni archivos privados y no modifican el Registro. Las pruebas con hardware se realizaron en un G502 HERO K/DA en modo integrado. El repositorio no incluye volcados del dispositivo, registros locales ni binarios de G HUB.

Diagnóstico y copia de solo lectura (esperar al proceso al usar PowerShell):

```powershell
.\G502Lite.exe --diagnose diagnostico.json | Out-Null
.\G502Lite.exe --backup copia.json | Out-Null
.\G502Lite.exe --startup-status inicio.json | Out-Null
```

`--self-test`, `--test-buttons`, `--test-lighting` y `--test-cycle` son pruebas de desarrollo: cambian temporalmente ajustes reales y después intentan restaurarlos. No son necesarias para el uso normal.

`--tray` abre únicamente el icono de bandeja. `--startup-enable resultado.json` y `--startup-disable resultado.json` permiten activar/desactivar el inicio automático de la misma forma que la casilla; devuelven el estado en el JSON indicado.

Referencias del protocolo: [Logitech HID++](https://github.com/Logitech/cpg-docs/tree/master/hidpp20), [libratbag](https://github.com/libratbag/libratbag) y [Solaar](https://github.com/pwr-Solaar/Solaar), consultadas para interoperabilidad. No se incorpora su código. El ejecutable no contiene ni carga código o binarios de LGHUB. Las dos imágenes del mouse son versiones editadas con la herramienta integrada de imágenes a partir de las capturas proporcionadas por el usuario: se quitaron textos, líneas, marcadores y logos; la aplicación dibuja encima sus propios controles. Están integradas en el ejecutable administrado como recursos de `Assets`.

### Triple clic izquierdo

En **Botones**, selecciona un botón distinto del principal, elige **Triple clic izquierdo · requiere G502 Lite** y pulsa **Guardar en el mouse**. Cada pulsación envía tres clics izquierdos completos en la posición actual del cursor; mantener pulsado no repite la secuencia. Funciona también con la ventana oculta en la bandeja.

Esta acción necesita G502 Lite en ejecución. Se guarda F24 (HID 0x73) como señal en el mouse: solo se procesa la interfaz del G502 HERO y una asignación leída de su memoria con CRC válido. No se interceptan teclados externos. F24 sigue llegando a Windows, por lo que no debe configurarse como atajo en otra aplicación. Al salir de G502 Lite el botón solo envía F24. Para quitarlo, guarda otra asignación en ese botón.

Se usa [SendInput de Windows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput); las aplicaciones ejecutadas con mayor nivel de privilegios pueden bloquearlo. La interpretación del triple clic depende de la aplicación de destino. La prueba de botones muestra la pulsación del disparador, no una confirmación de que el programa de destino aceptó los tres clics.

### Clic continuo mientras mantienes pulsado

En **Botones**, elige **Clic continuo mientras mantienes pulsado · requiere G502 Lite** y guarda. Envía un clic izquierdo inmediato y después a la velocidad elegida (10 clics/s de forma predeterminada), sin mover el cursor. Al soltar se detiene; no es un interruptor y no deja clics pendientes. También se cancela al desconectar un dispositivo, actualizar las asignaciones, cerrar G502 Lite o fallar la inyección de clics.

Funciona con G502 Lite abierto o en la bandeja y reserva F23 (HID 0x72) como señal del mouse. Evita utilizar F23 como atajo en otras aplicaciones. Tiene las mismas limitaciones de permisos de Windows que el triple clic; con G502 Lite cerrado solo envía F23. Cada clic incluye pulsación y liberación, sin dejar el botón izquierdo sostenido entre repeticiones.

La opción **Velocidad de clic continuo** permite elegir entre **1 y 50 clics por segundo**. Pulsa **Guardar velocidad**: se aplica a todos los botones de clic continuo y se conserva en `%LOCALAPPDATA%\G502Lite\click-speed.json`, incluso al actualizar la aplicación. Es independiente de **Guardar en el mouse** y de **Descartar cambios**, que afectan a las asignaciones. Cambiar la velocidad detiene una repetición activa; vuelve a pulsar para usarla. La frecuencia real puede variar según Windows y la aplicación de destino.
