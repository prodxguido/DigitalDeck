# DigitalDeck

Stream Deck virtual para Windows, pensado para vivir en una segunda pantalla mientras se produce
en Ableton Live. La especificación técnica completa está en [blueprint.html](blueprint.html).

Estado actual: **fase 4 del roadmap** (macros, atajos y pulido). Transporte, tempo, tracks, mezcla,
clips y vistas funcionan contra Live real; el editor de botón elige qué cargar de una lista sacada
del browser de Live; hay mixer táctil, asistente de voz, macros de varios pasos, botones que se
encienden con lo que está haciendo Live, y botones que mandan un atajo de teclado a cualquier
programa.

## Correr la app

Doble clic en **`Iniciar DigitalDeck.bat`**, en la raíz del proyecto: un botón que abre el deck.
Compara la fecha de los archivos fuente contra la del `.exe` compilado, compila sólo si algo cambió
y después lanza la app; si no hay nada nuevo arranca directo, sin los quince segundos del build. Si
la app ya estaba abierta la cierra antes, porque una copia abierta bloquea sus propios DLL. Cuando
la app arranca, el lanzador se cierra solo.

Para trabajar sobre el proyecto está el otro, **`Panel.bat`**: abre un panel con botones para compilar y
ejecutar, cerrar la app, correr los tests, correr la suite de humo contra Live e instalar el Remote
Script. Muestra además si la app, Live y el puente OSC están arriba.

Existe porque tres cosas se olvidan siempre y cuestan una vuelta entera: la app bloquea sus propios
DLL al compilar, toma el puerto 11001 que la suite de humo necesita, y Live sólo lee los Remote
Scripts al arrancar. El panel se ocupa de las tres.

A mano, si preferís la consola:

```bash
dotnet run --project src/DigitalDeck.App
```

En el primer arranque se crea un perfil de ejemplo con un botón de cada tipo que ya funciona.

Para armar el ejecutable que se reparte —uno solo, autocontenido, sin pedir un .NET instalado en la
máquina del otro— está `herramientas/empaquetar.py`:

```bash
python herramientas/empaquetar.py
```

Deja `dist/DigitalDeck.exe`, unos 120 MB. `--destino "otra carpeta"` lo pone en otro lado, `--zip`
arma además el comprimido al lado, y `--con-tests` corre toda la suite antes y no publica si algo
falla. El script existe porque la receta tiene banderas que no avisan cuando faltan: sin
`--self-contained` el `.exe` le pide un .NET a quien lo reciba, y sin `PublishSingleFile` quedan
297 archivos sueltos en vez de uno.

El primer grupo es **ABLETON**: adentro están las secciones del browser de Live (Instrumentos,
Efectos de audio, Efectos MIDI, Plugins, Baterías, Sonidos, Max for Live, User Library) como
carpetas. La de efectos de audio viene con veinte cargados — las dinámicas y EQ que van en casi
todos los canales, los de color, los dos de tiempo y el Audio Effect Rack; las demás vienen vacías
a propósito, porque qué instrumentos hay depende de los packs y plugins de cada uno, y se llenan
desde el picker del editor.

Esos veinte se escriben de una lista fija de nombres de fábrica, no de Live: en el primer arranque
Live suele estar cerrado y el Remote Script recién instalándose, así que no hay a quién
preguntarle. Si esta versión de Live no tiene alguno, el botón falla al apretarlo con el mensaje
del script y se re-apunta con el picker.

La primera carpeta del grupo es **Favoritos**, y se reescribe sola cada vez que se abre, con lo
más apretado del último tiempo. El ranking cuenta por *lo que hace* el botón y no por el botón, así
que una copia y su original suman al mismo lugar, y pesa menos lo viejo: cada treinta días sin tocarse,
una acción vale la mitad. Es una vista del ranking, no un lugar donde guardar cosas — lo que se
ponga ahí a mano se reemplaza la próxima vez que se abra.

Sólo puede existir una en todo el deck — el ranking es del programa entero, no por perfil — y no
hace falta quedarse con la que trae el grupo ABLETON: **clic derecho en una celda vacía → Agregar
carpeta de favoritos** la crea donde quieras, y si ya tenías una en otro lado te avisa en vez de
duplicarla.

## Conectar con Ableton Live

Ableton no tiene API pública: la integración pasa por un Remote Script propio en Python que corre
adentro de Live y habla OSC por loopback. Se instala una sola vez.

**El script viaja adentro del `.exe` y la app lo instala sola.** En el primer arranque, si no está
puesto, se abre una ventana que dice en qué estado está, dónde lo va a poner, y lo copia con un
botón. Después queda a mano haciendo clic en el indicador de Ableton de la barra superior. Es lo
que hace que una copia del ejecutable sirva en una máquina donde no hay ni repositorio ni consola.

A mano, con Live cerrado, sigue existiendo:

```bash
python src/DigitalDeck.RemoteScript/install.py
```

Anda en Windows y en Mac: sabe que la User Library cuelga de Documents en uno y de Music en el
otro, y avisa si Live está abierto en vez de dejar a Live con el código viejo en memoria y el
nuevo en disco.

En los dos casos, después: en Live, **Preferencias → Link, Tempo & MIDI → Control Surface**, elegí
`DigitalDeck` en un slot libre. El indicador de la barra superior tiene que ponerse en verde.

Detalle completo, diagnóstico y estructura del script en
[src/DigitalDeck.RemoteScript/README.md](src/DigitalDeck.RemoteScript/README.md).

**Live sólo lee los Remote Scripts al arrancar.** Cualquier cambio en el `.py` necesita reiniciar
Live entero.

## Logos e imágenes en los botones

Cada botón puede llevar una imagen propia, además de los íconos del set. Al elegirla, la app la
**recorta hasta el dibujo** — el margen transparente o de color plano que traen casi todos los
logos que uno se baja — y la encuadra en un PNG cuadrado de 256 px. Es lo que hace que veinte logos
de veinte fuentes distintas se vean del mismo tamaño y centrados de verdad, y no cada uno como
venía. Sin el recorte, un logo con la mitad del archivo en aire aparece chiquito y corrido, porque
queda centrado sobre su lienzo y no sobre lo que tiene dibujado.

Dos encuadres, a elección: **Ajustar** (la imagen entera, con aire alrededor — para logos) y
**Rellenar** (llena el cuadrado recortando lo que sobra — para fotos). Se puede cambiar de uno a
otro cuando quieras: la app guarda también el original, así que re-encuadrar vuelve a partir de la
imagen y no de un recorte sobre otro recorte.

Cuando hay imagen, el texto del botón se dibuja sobre un velo oscuro, porque un logo claro con el
label encima no se lee.

## Plantillas

En [plantillas/](plantillas/LEEME.md) hay grupos ya armados para importar: FabFilter, Waves,
Valhalla, u-he, Soundtoys, Roland Cloud, iZotope y D16 — 249 plugins en total, repartidos en
carpetas por categoría — y `ableton-dispositivos`, que no es una marca sino los 62 dispositivos que
ya trae Live: efectos de audio, efectos MIDI y los Max for Live (LFO, Shaper, Envelope Follower).

Se instalan desde el menú ☰ con **Instalar plantillas…**, y al elegirlas la app te pregunta cómo
las querés:

- **Cada una como una página nueva** — lo que viene marcado. Se suma una pestaña por marca con los
  plugins ya desplegados, que es lo que una estantería de quince plugins realmente es: una pantalla,
  no una tecla.
- **Como un grupo en la página que estoy usando** — cae en la primera celda libre y ocupa un botón,
  para armar una sola página con las tres marcas que usás de verdad.

En los dos casos entran al perfil abierto y no se mueve nada de lo que ya estaba. Un `.deckpack`
suelto se trae igual con **Importar paquetes…**, que hace la misma pregunta; el clic derecho sobre
una celda vacía sigue teniendo **Importar paquetes acá**, que ya es una respuesta. Un respaldo es
otra cosa, y para eso está **Importar paquete como perfil**, que trae el deck entero aparte.

No llevan rutas de instalación: cada botón guarda el nombre del plugin y la sección `plugins` del
browser, así que si el plugin está instalado y Live lo escaneó, anda tal cual viene. El detalle de
cómo armar una nueva está en ese mismo LEEME.

## Mixer táctil

Menú ☰ → **Mixer de Ableton**, o un botón con esa acción, abre una ventana aparte con un fader por
canal del set abierto: las tracks en el orden en que están, los grupos con su sangría, los returns
y el master. Está pensada para arrastrarla a una pantalla táctil y dejarla ahí mientras el deck
queda en el otro monitor, y los faders tienen el tamaño que necesita un dedo, no un mouse.

Mover un fader en Live mueve el de la ventana y al revés. Live sólo empuja esos cambios mientras la
ventana está abierta —cerrarla corta la suscripción— así que un deck con el mixer cerrado no paga
nada por él. Sólo puede haber una abierta: cada copia le pediría a Live que empuje cada movimiento
otra vez, para mostrar lo mismo dos veces.

No tiene medidores de nivel, a propósito.

## Asistente de voz

Apretás un atajo, decís el nombre de un botón que esté en pantalla, y se aprieta. Todo el
reconocimiento pasa en esta máquina: no sale audio a ningún lado, y el micrófono se abre solamente
mientras dura la escucha.

El modelo de voz (`vosk-model-small-es-0.42`, ~38 MB comprimido) viaja **adentro del .exe**. En el
primer arranque la app pregunta si querés instalarlo; si decís que no, queda un botón **Instalar el
asistente de voz** en el menú `⋯` › **Asistente de voz**. Se desempaqueta a
`%APPDATA%\DigitalDeck\voice\` y de ahí en más el `⋯` › **Asistente de voz** › activar lo enciende.

El zip embebido está en `src/DigitalDeck.Integrations/Voice/Assets/`. Para cambiar de modelo (por
ejemplo al grande), reemplazá ese archivo por otro `.zip` que tenga una sola carpeta raíz con `am`,
`conf` y `graph` adentro — `EmbeddedVoiceModelInstaller` la busca por prefijo, no por nombre.

Ahí mismo se elige el micrófono y el atajo. El medidor de nivel es la forma honesta de elegir el
micrófono: hablá y mirá si la barra se mueve, en vez de adivinar leyendo una lista de nombres.

Vale la pena saber tres cosas antes de pelearse con esto:

- **Windows tiene dos micrófonos "por defecto" distintos.** El que se cambia en Configuración es
  el de *Console*; el panel viejo de Sonido tiene otro aparte para *Communications*. DigitalDeck
  usa el primero, que es el que vos creés estar eligiendo.
- **No compite con ASIO.** La captura va por WASAPI en modo compartido, que es un camino distinto
  al driver. Live puede estar abierto, grabando, sobre la misma placa. La única combinación que no
  funciona es un micrófono enchufado a una interfaz cuyo driver le esconde las entradas a Windows
  mientras ASIO las tiene; la solución ahí es cualquier otro micrófono.
- **Escucha sólo lo que está en pantalla.** Los botones del grupo donde estás parado son el
  vocabulario. Si dos se llaman parecido y no queda claro cuál dijiste, no aprieta ninguno.

## Actualizar sin perder nada

La configuración no viaja con el ejecutable: vive en `%APPDATA%\DigitalDeck\deck.db`, aparte. Se
puede reemplazar el `.exe` por una versión nueva las veces que haga falta y el deck queda igual —
no hay nada que restaurar. Cuando una versión cambia el esquema, la app lo migra sola al arrancar,
con los datos donde estaban.

Lo único que puede romper un deck es una migración con un error adentro, así que **antes de migrar
la app deja una copia** en `backups/`, con la versión y la fecha en el nombre (`deck-v1-*.db`). Se
guardan las últimas diez, no la aprieta nadie, y si la copia no se puede escribir la migración no
corre. La copia se pide con `VACUUM INTO` y no copiando el archivo, porque la base está en WAL: los
últimos cambios todavía no están en `deck.db` y un copiar-y-pegar se los perdería.

Para mover un perfil a otra máquina, o compartirlo, sigue estando **Exportar perfil**, que escribe
un `.deckpack` con botones, carpetas, acciones e íconos adentro.

## Tests

```bash
dotnet test
```

El Remote Script se testea aparte, sin dependencias, contra un Live Object Model falso:

```bash
cd src/DigitalDeck.RemoteScript
python -m unittest discover -s tests
```

Y la suite de humo contra Ableton real, que necesita Live abierto con el script activado:

```bash
$env:DIGITALDECK_LIVE=1; dotnet test --filter FullyQualifiedName~AbletonSmoke --logger "console;verbosity=detailed"
```

Y la del micrófono, que abre uno de verdad y reporta a qué ritmo y con cuánto nivel entra:

```bash
$env:DIGITALDECK_MIC=1; dotnet test --filter FullyQualifiedName~VoiceInputSmoke --logger "console;verbosity=detailed"
```

Correla dos veces —con Live cerrado y con Live abierto sonando por la misma placa— si querés
comprobar en tu máquina que WASAPI no le pelea el dispositivo a ASIO. Los tests del reconocedor se
saltean solos cuando no hay un modelo instalado.

Sin esa variable esos tests se saltean, así que `dotnet test` queda verde en una máquina sin
Ableton. Correrla contra cada versión nueva de Live **antes** de recomendar el update es lo que
pide §26 del blueprint: la integración se apoya en comportamiento que Ableton no documenta ni
promete mantener.

## Qué hay implementado

| Área | Estado |
|---|---|
| Grilla configurable con tamaños de botón discretos | Listo |
| Grupos anidados sin límite de profundidad | Listo |
| Drag & drop: mover, intercambiar, soltar dentro de un grupo | Listo |
| Botones de carpeta / archivo / programa | Listo |
| Botones de página web | Listo |
| Perfiles y páginas múltiples | Listo |
| Íconos (glifos del sistema o imagen propia) y colores | Listo |
| Export / import `.deckpack` | Listo |
| Multi-monitor: recuerda pantalla, posición y fullscreen | Listo |
| Remote Script propio + puente OSC con Live | Listo |
| Acciones de Ableton: transporte, tempo, tracks, mezcla, clips, vistas | Listo |
| Indicador de conexión con Live | Listo |
| Browser de Live vía OSC (listar, buscar, cargar) | Listo |
| Botón que carga un rack, preset o plugin en la track seleccionada | Listo |
| Picker del browser de Live en el editor: navegar, buscar y elegir | Listo |
| El Remote Script viaja en el `.exe` y la app lo instala sola | Listo |
| Botón que hace varias cosas de una: pasos en orden, con espera entre uno y otro | Listo |
| Los botones se encienden con lo que está haciendo Live | Listo |
| Mixer táctil: un fader por canal del set abierto | Listo |
| Asistente de voz por push-to-talk, todo en la máquina | Listo |
| Botón que manda un atajo de teclado a otro programa | Listo |

### Un botón, varias acciones

En el editor, la opción **Varias acciones** convierte el botón en una secuencia. Cada paso se elige
con el mismo editor que un botón suelto —una web, una carpeta, un programa, un comando de Live,
cargar un dispositivo—, se ordena con las flechas, y puede llevar una espera antes de correr, para
darle tiempo a un programa a abrirse antes del paso que lo necesita.

Un «arrancá la mañana» es exactamente eso: la playlist de YouTube, el diario, Live y Spotify, en
orden, de un botón. Por defecto **sigue aunque un paso falle** y avisa cuáles fallaron al terminar:
que Spotify no esté instalado no es motivo para quedarte sin el resto. Una secuencia no puede tener
otra secuencia adentro, que es lo que evita que un deck se arme un ciclo infinito.

### Los botones muestran en qué estado está Live

Un botón de play se enciende mientras el set suena, el del metrónomo mientras el click está
puesto, el de grabar mientras el Arrangement está armado, y uno de mute, solo o arm mientras ese
canal lo está. Se pinta con el color que vos le pusiste al botón, más fuerte y con el borde
encendido, así un botón prendido sigue siendo reconocible como ese botón.

Un botón de mute apunta por defecto a **la track seleccionada**, y la luz lo sigue: la misma tecla
se enciende o se apaga según en qué canal estés parado en Live.

Nada se enciende mientras la app no sepa. Si Live está cerrado, si el puente todavía no levantó, o
si el Remote Script es viejo, los botones quedan apagados en vez de mostrar lo último que
escucharon — una luz que miente es peor que ninguna. Por eso también, cuando se pierde la conexión
con Live, se apagan todos.

El Session record no se enciende: el Remote Script no lo observa, así que cualquier cosa que
mostrara sería una suposición.

### Acciones de Ableton disponibles

Definidos en [AbletonCommands.cs](src/DigitalDeck.Core/Model/Actions/AbletonCommands.cs):

| Grupo | Qué cubre |
|---|---|
| Transporte | play, stop, continue, stop all clips, los dos records, capture MIDI, undo/redo |
| Tempo | fijar, mover, tap, metrónomo, loop |
| Tracks | seleccionar, mute/solo/arm, volumen, paneo, crear, duplicar, eliminar |
| Clips | disparar y detener clip, disparar escena |
| Vistas | Session, Arrangement, el toggle entre las dos, detalle, browser |
| Zoom | acercar y alejar en el tiempo o en el alto de los tracks, y niveles fijos |
| Archivo | exportar audio/video, guardar, guardar como |

**Archivo va por otro lado.** Exportar y guardar no existen en la API de Live, así que esos botones
traen Live al frente y le mandan su atajo de teclado de fábrica (Ctrl+Shift+R, Ctrl+S, Ctrl+Shift+S)
con `SendInput`. No necesitan el puente OSC, sólo Live abierto, y andan con cualquier usuario —
salvo que se haya cambiado el atajo en las preferencias de Live. El código está en
[WindowsLiveKeyboard.cs](src/DigitalDeck.Integrations/Windows/WindowsLiveKeyboard.cs).

Para abrir un proyecto entero: el chip **Abrir proyecto de Live** en el editor toma un `.als` y lo
abre con el shell de Windows, como un doble clic. Sirve para decirle el nombre del proyecto por voz.

### Zoom

Live no tiene "poné el zoom en X": su API sólo mueve el zoom un paso por vez y nunca dice en qué
nivel quedó. Los botones de **nivel fijo** lo resuelven yendo primero hasta el fondo y contando
pasos desde ahí, así que cada botón siempre llega al mismo lugar venga de donde venga — que es lo
que hace que valga la pena tener tres o cuatro, uno por momento de trabajo (todo el arreglo, la
sección, el detalle). Los de *acercar* y *alejar* son relativos, como apretar `+` o `-` varias
veces. El zoom se aplica a la vista que estés mirando, Session o Arrangement.

Como la API no dice qué dirección significa "más cerca", el mapeo está en una sola tabla
(`ZOOM_AXES` en [view.py](src/DigitalDeck.RemoteScript/DigitalDeck/handlers/view.py)): si alguna
sale al revés, se da vuelta ahí y nada más.

Un botón apunta por defecto a **la track seleccionada**, no a un número fijo: se configura una vez
y se usa durante meses, y "la track en la que estoy" envejece mucho mejor que "la track 4", que
deja de significar lo mismo apenas insertás una track antes.

### Un botón que manda un atajo de teclado

La salida de emergencia, y por eso es la última opción del editor: un programa que no tiene ninguna
otra forma de entrada se maneja como lo maneja una persona, con su propio atajo. En el editor,
**Atajo de teclado** pide dos cosas —la combinación, que se captura apretándola, y el programa— y
al apretar el botón DigitalDeck trae ese programa al frente y le manda las teclas.

El programa es obligatorio, y ese es el detalle que hace que funcione. Al tocar un botón del deck
el foco del teclado lo tiene el deck, así que un atajo sin destino le llegaría a la propia app.
Se escribe como empieza el nombre del proceso —`Ableton Live`, `chrome`, `Photoshop`— porque casi
todos llevan su versión en el nombre y un botón configurado una vez tiene que sobrevivir a la
próxima actualización del programa que maneja.

Dos cosas que conviene saber: si el programa está abierto **como administrador** y DigitalDeck no,
Windows no deja mandarle teclas y el botón lo dice en vez de no hacer nada; y las teclas de
puntuación no se aceptan, porque su código cambia entre distribuciones de teclado y el mismo botón
caería en otro lado en otra máquina. Letras, números y las F andan en todas.

Es el mismo mecanismo que ya usaban Exportar, Guardar y Guardar como de Ableton — esos tres botones
son este, con la combinación y el programa ya puestos.

## Tamaño de la grilla

Cada página tiene su propia cantidad de columnas y filas — una con pocos botones se ve mejor chica,
sin que eso achique las demás. En **modo edición**, la barra de arriba de la página (donde está la
migaja de pan) muestra los tamaños más comunes como botones: **4×3, 6×4, 8×5, 10×6**. Tocar uno
cambia la grilla al toque; el que está resaltado es el tamaño actual. Están visibles tanto parado en
la página como adentro de cualquier grupo, porque la grilla es de la página entera — cambiarla
cambia cómo se ven todos los grupos que viven en ella, no sólo el que estés mirando.

Para un tamaño que no es ninguno de esos cuatro, el botón con el ícono de engranaje al final abre
**Configurar página**, con columnas y filas libres entre 2–16 y 2–12. Achicar la grilla —desde los
botones rápidos o desde ese diálogo— avisa antes si algún botón quedaría fuera de la vista, para
moverlo primero.

## Atajos

| Tecla | Acción |
|---|---|
| `Ctrl` + `F` | Buscar un botón en todo el perfil |
| `F11` | Pantalla completa en el monitor actual |
| `Esc` | Cerrar la búsqueda, o salir de pantalla completa |
| `Ctrl` + `E` | Alternar modo edición |
| `Backspace` | Volver al grupo anterior |
| `Ctrl` + `Alt` + `Espacio` | Escuchar un comando de voz (configurable) |

La búsqueda mira todas las páginas del perfil y todos los grupos, sin importar la profundidad:
escribís parte del nombre, cada resultado muestra la ruta donde está, y `Enter` toma el primero.
Elegir un resultado lleva el deck hasta ahí y después dispara el botón —en modo edición sólo te
deja parado ahí—, así que un botón olvidado cuesta un atajo en vez de una recorrida. Ignora mayúsculas y acentos, y si el nombre no dice nada
también busca en lo que el botón hace (la ruta que abre, el dispositivo que carga).

En modo edición: clic en una celda vacía crea un botón, clic en un botón lo edita, clic derecho
abre el menú (duplicar, tamaño, exportar grupo, eliminar) y se puede arrastrar para reordenar.

## Estructura

```
Iniciar DigitalDeck.bat      Lanzador de un botón (doble clic)
Panel.bat                    Panel de control para desarrollar (doble clic)
herramientas/
  iniciar.ps1                Implementación del lanzador
  panel.ps1                  Implementación del panel
  empaquetar.py              Arma el .exe autocontenido que se reparte
  empaquetar-mac.py          Arma el DigitalDeck.app de macOS, desde cualquier máquina
  generar-iconos-plantillas.py  Rearma los PNG de las plantillas desde sus manifests
src/
  DigitalDeck.Core/                 Modelo, orden fraccional, motor de grilla, despacho de acciones
  DigitalDeck.Data/                 SQLite, almacén de íconos, formato .deckpack
  DigitalDeck.ViewModels/           Lo que el deck sabe hacer, sin saber quién lo dibuja
  DigitalDeck.Integrations/         Puente OSC con Ableton, instalador del script, abrir carpetas y webs
  DigitalDeck.Integrations.Windows/ Win32 y captura de micrófono: lo único que ata a Windows
  DigitalDeck.App/                  WPF: la app de todos los días
  DigitalDeck.Avalonia/             El mismo deck, dibujado por Avalonia — el camino a Mac
  DigitalDeck.RemoteScript/         Python — corre DENTRO de Ableton, se instala aparte
tests/
  DigitalDeck.Tests/               Core, Data y los view models
  DigitalDeck.App.Tests/           Ventanas de WPF y el view model contra una base de verdad
  DigitalDeck.Integrations.Tests/  Codec OSC, adapter, y la suite de humo contra Live
  DigitalDeck.Avalonia.Tests/      El shell de Avalonia, headless: sin escritorio y sin ventana
```

Los cuatro primeros son `net8.0` a secas, y eso no es cosmético: ahí no se puede referenciar WPF ni
por accidente, así que el compilador es el que sostiene la regla en vez de la disciplina.

Los datos del usuario viven en `%APPDATA%\DigitalDeck` en Windows y en
`~/Library/Application Support/DigitalDeck` en macOS (`deck.db` + `assets/icons`).

## El port a Mac

La app de todos los días sigue siendo la de WPF. Al lado hay un segundo shell, en Avalonia, que
ata **los mismos view models** contra **la misma base**: no reimplementa nada, y ese es el punto —
una regla con dos copias es una regla que se despega.

Qué anda hoy en el shell de Avalonia: la grilla con los botones en sus celdas, la navegación entre
páginas y grupos, el **modo edición** (crear, editar, duplicar y eliminar botones), el **editor**
con nombre, color, ícono y comandos de Live, el **mixer táctil**, y el **puente con Live de verdad**
—los mismos comandos, el mismo socket, los puertos que configuraste—. Recuerda además en qué
pantalla estaba, en la misma clave de ajustes que usa la app de WPF.

También instala el Remote Script —que es lo que hace existir el puente, y lo único que una Mac
recién sacada de la caja no puede saltear—, importa y exporta `.deckpack`, instala las plantillas
que viajan adentro de la app, configura páginas, y busca botones en todo el perfil con `Ctrl+F`
(`Cmd+F` en Mac).

Y elige dispositivos del browser de Live desde el editor, que es lo que hace que un botón cargue:
lleva un nombre que Live dijo, no uno escrito a mano.

Y arma botones de varias acciones, con su orden y sus esperas.

Qué no: los botones que mandan un atajo de teclado a otro programa —en macOS eso es un evento de
Quartz con el permiso de accesibilidad detrás— y los ajustes de voz, porque el asistente sigue
siendo sólo de Windows: NAudio no cruza. Un botón que ya tiene una de esas dos cosas se edita sin
perderla, y hay un test que lo defiende.

Para probarlo en una Mac hay un empaquetador que corre desde cualquier lado, porque .NET compila
de forma cruzada:

```bash
python herramientas/empaquetar-mac.py
```

Deja `dist/mac/DigitalDeck.app` (~105 MB, Apple Silicon; `--arch x64` para una Mac Intel). Lo que
**no** hace es firmarlo ni notarizarlo, así que del otro lado hace falta un `chmod +x` sobre el
ejecutable —el bit no sobrevive el zip— y un `xattr -dr com.apple.quarantine`, porque sin firma
Gatekeeper dice que la app está dañada, que es su forma de decir que no sabe quién la hizo. El
script lo recuerda al terminar.

Lo que hizo posible todo eso fue sacar del camino tres cosas, en este orden:

1. **Win32 fuera de App y Core.** Todo lo que le pide algo al escritorio pasa por `IMonitors`,
   `IWindowEffects` e `IGlobalHotkey`, con la implementación de Windows detrás.
2. **Los view models fuera de WPF.** No exponen brushes ni geometrías: dan el color como texto y el
   ícono como nombre, y cada shell los convierte.
3. **Integrations partido en dos.** El puente con Live no tenía una línea de Win32; era inportable
   sólo por el proyecto en el que estaba.

Las reglas que los dos shells comparten viven en Core y tienen tests propios: dónde cae cada celda
de la grilla (`DeckGridMetrics`), qué pantalla es la que se recuerda (`MonitorMatch`), qué botón se
enciende (`LiveButtonLight`) y el set de íconos como path data (`DeckIconPaths`).

## Decisiones que conviene no revertir sin leer el blueprint

### De la fase 1

- **La grilla no compacta.** Una celda vacía es una decisión de layout del usuario, no un hueco a
  cerrar; los botones se quedan donde se los pone.
- **El orden entre hermanos usa claves fraccionales**, así reordenar por drag & drop es un solo
  `UPDATE` en vez de renumerar todo.
- **SQLite guarda el árbol plano con `parent_id`; el JSON anidado existe sólo dentro del
  `.deckpack`**, como formato de intercambio.
- **Los íconos son archivos nombrados por hash de contenido**, no blobs en la base.

### De la fase 2

- **El LOM sólo se toca desde el hilo de Live.** Llamarlo desde un worker no lanza una excepción:
  cierra Live. Por eso el script no tiene hilo de recepción — el socket es no bloqueante y se drena
  desde el tick del control surface.
- **OSC sólo por `127.0.0.1`**, con bind explícito a loopback y nunca a `0.0.0.0`. Sin eso,
  cualquier máquina de la red podría disparar acciones en el Live Set (§20).
- **Core no conoce OSC.** El catálogo de comandos no tiene direcciones; el mapeo enum → dirección
  vive en [AbletonCommandMap.cs](src/DigitalDeck.Integrations/Ableton/AbletonCommandMap.cs). Un
  test recorre el enum entero para que no se pueda agregar un comando y olvidar mapearlo.
- **Los comandos son fire-and-forget.** UDP no confirma nada y el script sólo habla si algo salió
  mal. De ahí dos consecuencias que no se pueden tapar: una acción se rechaza *antes* de mandarla
  si el puente está caído, y los errores que llegan tarde suben por `IAbletonClient.ScriptFailed`
  hasta la barra de estado. Sin eso, un botón que falla adentro de Live se vería igual que uno que
  funcionó.
- **El codec OSC está escrito a mano en los dos lados** en vez de tomar un paquete. Son ~150 líneas
  por lado, no hay dependencias, y tener las dos mitades legibles juntas vale más acá que la
  librería que nos ahorraríamos.
- **Los puertos no son los de AbletonOSC, y se pueden cambiar.** Live carga los dos scripts a la
  vez sin drama, pero el puerto UDP lo puede tener uno solo: el segundo en arrancar se queda sordo
  en silencio. DigitalDeck usa 11010/11011 y le deja a AbletonOSC los 11000/11001 que están en toda
  su documentación. El par se edita desde la app, y el instalador lo escribe dentro del script al
  copiarlo, que es lo que impide que las dos mitades queden en puertos distintos. Ese desajuste no
  se puede detectar por la red —no llega nada en ninguna dirección— así que se detecta leyendo el
  archivo del disco.

- **La compatibilidad del script se chequea por versión mayor.** Un 2.1.0 contra una app que
  esperaba 2.2.0 es válido; un 1.x no. El handshake reporta las dos versiones y la de Live.
  Con un 2.1.0 el picker funciona pero no abre nada: le falta el campo de «tiene hijos» y cae
  al comportamiento viejo. Reinstalá y reiniciá Live.
- **Cargar un dispositivo va siempre a la track seleccionada en Live.** No es una limitación que
  se pueda esquivar: `browser.load_item()` no tiene parámetro de destino. Es además el gesto que el
  botón reemplaza — arrastrar desde el browser de Live hace exactamente eso. Para apuntar a una
  track fija se encadena un botón `SelectTrack` antes.
- **El Remote Script viaja embebido en el ensamblado, no al lado del `.exe`.** Es la única forma de
  que una copia suelta del ejecutable sirva: sin eso, la mitad del puente que corre adentro de Live
  nunca llega a la máquina, Live no muestra `DigitalDeck` en Control Surface, y la app sólo puede
  decir que no encuentra a Live — el único fallo de esta app sin causa visible y sin salida desde
  adentro. Un test recorre la carpeta del script y verifica que no falte ningún `.py`, y otro que la
  versión embebida sea la que el adapter espera.

- **`is_folder` no quiere decir «tiene hijos».** Medido contra Live 12.3: los 47 ítems del nivel
  superior de Efectos de audio lo reportan en `False`, «Audio Effect Rack» incluido — y ese tiene
  seis carpetas de racks del usuario adentro. Un picker que navegara por ese flag no llegaría a
  ninguno. Por eso el script manda un quinto campo aparte que dice si entrar lleva a alguna parte,
  y por eso elegir y entrar son dos gestos distintos: 44 de esos 47 son cargables **y** tienen
  presets abajo al mismo tiempo, así que si un solo click tuviera que significar las dos cosas,
  cargar un Compressor costaría dos. El click en la fila elige; el chevron de la derecha entra. Y
  la fila del dispositivo aparece repetida arriba de su propio listado, porque entrar no puede
  dejar afuera al dispositivo mismo.

- **Las consultas al browser esperan respuesta; los comandos no.** Es la única parte del puente que
  espera, y UDP pierde datagramas sin avisar. La correlación es por dirección más el eco que manda
  el script (categoría y path, o categoría y query), porque el protocolo no tiene request id; el
  timeout reintenta una vez, ya que preguntar dos veces cuesta una recorrida acotada y no preguntar
  de nuevo cuesta un picker vacío sin explicación. Un `/digitaldeck/error` sobre esa dirección corta
  la espera en vez de dejarla cumplir el reloj.

- **El Browser se consulta, no se lee del disco.** `load_item()` sólo carga lo que Live ya indexó,
  así que listar `.adg` sueltos de la User Library produciría ítems que después no se pueden
  cargar. Preguntándole al Browser, todo lo listado es cargable por construcción y viene con su
  URI — que resultó ser único por ítem (33 racks, 33 URIs), así que sirve para desambiguar dos
  presets con el mismo nombre en vez de cargar el que salga primero.

### Del paso 1 del port a Mac

- **Ni `DigitalDeck.App` ni `DigitalDeck.Core` tienen una sola llamada a Win32.** Todo lo que le
  pide algo al escritorio pasa por tres interfaces de Core — `IMonitors` (enumerar pantallas y mover
  una ventana en píxeles físicos), `IWindowEffects` (esquinas redondeadas, y el overlay que no roba
  el foco) y `IGlobalHotkey` (reservar una combinación en todo el sistema) — con la implementación
  de Windows detrás, en `DigitalDeck.Integrations/Windows`. El único lugar de App que nombra un tipo
  de Windows es `App.xaml.cs`, que es donde se elige la plataforma y es su trabajo.
- **Los handles de ventana viajan como `nint` opaco.** Es un `HWND` hoy y un `NSWindow` después;
  ninguna de las tres interfaces mira adentro, sólo se lo devuelve a quien lo emitió.
- **Elegir monitor no es código de plataforma.** Cuál de las pantallas de hoy es aquella en la que
  quedó la ventana es lógica común, y vive en
  [MonitorMatch.cs](src/DigitalDeck.Core/Services/MonitorMatch.cs), con tests. Antes estaba adentro
  del código Win32, donde sólo se podía probar enchufando y desenchufando monitores de verdad.
- **`IGlobalHotkey` tiene un método que huele a Windows, `OnHostMessage`, y está bien así.** Windows
  entrega un atajo como mensaje de ventana y sólo el shell tiene esa cola. Una implementación cuya
  plataforma los entregue de otra forma lo ignora y dispara `Pressed` por su cuenta.
