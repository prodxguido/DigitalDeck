# DigitalDeck · panel y barra

## Dos formas de usar el mismo deck

- **Panel:** grilla para una pantalla de 5–7 pulgadas; F11 ocupa el monitor actual. Los botones conservan sus posiciones, tamaños, colores y grupos.
- **Barra:** el selector visible **Vista ▾ → Barra arriba / Barra abajo** muestra el mismo contenido en una o dos hileras. Siempre queda por encima de las ventanas normales.

**Vista ▾** está disponible tanto en el panel como en la barra. Permite volver a **Panel · pantalla táctil** y, mientras la barra está activa, elegir **Una hilera / Dos hileras**. En la posición inferior también permite elegir **Abajo · encima de Windows** o **Abajo · sobre Windows**. La opción actual aparece marcada.

En la barra, el menú **⋯** permite elegir **Arriba / Abajo** y **Una hilera / Dos hileras**. Abajo se ubica inmediatamente encima de la barra de Windows, dentro del área útil del monitor. No reemplaza la barra de Windows ni reserva espacio en Ableton: se superpone a las ventanas.

En la barra inferior, los botones son cuadrados y muestran el icono del dispositivo; el nombre queda en el tooltip. Así entran muchos más plugins en una sola hilera. El perfil se abre desde el icono de usuario y el workflow queda como selector corto de página. El ojo cambia la vista y los tres puntos reúnen las opciones menos frecuentes.

La altura de una hilera toma como referencia la barra de tareas de ese monitor, con un mínimo de 40 unidades de interfaz. Si Windows oculta automáticamente la barra, o la ubica verticalmente, se usa una referencia de 48 unidades. Dos hileras duplican esa altura.

Las flechas y la rueda permiten recorrer los botones que exceden el ancho. También se admite desplazamiento táctil horizontal. **DD** o **Esc** vuelve al panel. **+** abre el panel para agregar un botón; **⋯ → Editar este workflow** permite reorganizarlo.

## Perfiles y workflows

Los dos selectores de la barra cambian el **perfil** y la **página**. Una página puede contener los plugins de mezcla, otra instrumentos de producción y otra controles de grabación. Cada perfil recuerda su última página. Los grupos siguen siendo navegables en ambos modos.

La barra utiliza las acciones reales del perfil: no mantiene una copia separada de los botones. Para elegir los plugins instalados, usar el editor y el browser de Ableton con el Remote Script conectado. Los perfiles existentes no se reemplazan.

Una biblioteca nueva incluye páginas de Producción y Mezcla con efectos nativos y transporte. Son puntos de partida editables; la disponibilidad de cada dispositivo depende de la edición de Live.

## Diseño

Paleta grafito con acento lima, superficies con relieve sutil, iconos con color por botón y señales de estado real de Live. Las teclas de la barra tienen respuesta al pasar el cursor, presionar y recibir foco. Conservan los iconos personalizados. El signo de exclamación indica un error; su detalle se consulta sobre el indicador de conexión.

## Estado del desarrollo

Los cambios más recientes se revisaron como código y XML, sin compilar ni ejecutar pruebas, por pedido del usuario. El ejecutable previamente abierto no refleja necesariamente esta versión.

El MD original no incluía binarios: faltan los paquetes `.deckpack`, sus imágenes y el modelo Vosk de voz. Se conserva el código que usa esos recursos, pero no se considera validada la voz ni la carga de dispositivos contra una sesión real de Ableton.

Después de un reinicio intencional, DigitalDeck conserva la base vacía y no vuelve a crear el deck de ejemplo automáticamente.
