# DigitalDeck Remote Script

Este es el único pedazo del repo que **no es .NET**: es Python, y corre *adentro* de Ableton Live.
Vive acá por trazabilidad — se versiona junto al código que lo consume — pero se instala aparte,
copiándolo a la carpeta de Remote Scripts de Ableton.

Traduce mensajes OSC que llegan por loopback a llamadas del Live Object Model. Es el mismo enfoque
que usa [AbletonOSC](https://github.com/ideoforms/AbletonOSC), con el que comparte el espacio de
direcciones `/live/...` para que el conocimiento sea transferible; lo propio de DigitalDeck vive
bajo `/digitaldeck/...`.

## Instalación

```bash
python src/DigitalDeck.RemoteScript/install.py
```

Con Live cerrado. Después:

1. Abrí Ableton Live.
2. **Preferencias → Link, Tempo & MIDI → Control Surface**: elegí `DigitalDeck` en un slot libre.
3. El indicador de DigitalDeck debería pasar a verde.

Si preferís hacerlo a mano, copiá la carpeta `DigitalDeck/` (la de adentro, no ésta) a:

```
%USERPROFILE%\Documents\Ableton\User Library\Remote Scripts\DigitalDeck
```

**Live sólo lee los Remote Scripts al arrancar.** Cualquier cambio en el `.py` necesita reiniciar
Live, no alcanza con deseleccionar y volver a seleccionar el Control Surface.

## Diagnóstico

Todo lo que el script tiene para decir va al log de Live, prefijado con `DigitalDeck:`.

```
%APPDATA%\Ableton\Live <versión>\Preferences\Log.txt
```

Si el script no aparece en el dropdown de Control Surface, el error de importación está ahí.

Para ver el espacio de direcciones completo tal como quedó registrado, mandá
`/digitaldeck/addresses` — contesta con todas las direcciones separadas por saltos de línea.

## Puertos

| | Por defecto |
|---|---|
| Recibe | `127.0.0.1:11010` |
| Envía | `127.0.0.1:11011` |

**Distintos a los de AbletonOSC a propósito.** AbletonOSC usa 11000 y 11001, y esos números están
en toda la documentación que la comunidad escribió sobre él. Live carga los dos Control Surfaces
sin problema —hay varios slots y no compiten— pero dos procesos no pueden bindear el mismo puerto
UDP: el segundo en arrancar se queda sordo, sin decir nada. Así que DigitalDeck se corre, y una
instalación de cada uno funciona sin que nadie configure nada.

El par vive en `config.py`, y **lo reescribe el instalador de la app** con lo que esté configurado
ahí. Es lo que evita que las dos mitades del puente terminen en puertos distintos — un estado que
por la red es invisible, porque no llega nada en ninguna dirección y se ve igual que Live cerrado.
Por eso la app lo detecta leyendo el archivo, no preguntando.

El handshake informa el par en el que está escuchando, como confirmación y para el log.

El bind es a `127.0.0.1` explícito, nunca `0.0.0.0`: si no, cualquier máquina de la red podría
disparar acciones en el Live Set.

## El formato de las respuestas del Browser

`list` y `search` contestan en la misma direccion en la que se les pregunto, ecoando lo que
identificaba la pregunta -- categoria y path, o categoria y query. Ese eco es lo unico que tiene la
app para saber que respuesta corresponde a que pregunta: el protocolo no lleva request id.

Cada linea de resultado son cinco campos separados por tabulacion:

```
nombre  uri  esCargable  esCarpeta  tieneHijos
```

Los dos ultimos no son lo mismo, y confundirlos costaria el picker entero. Medido contra Live 12.3,
`is_folder` viene en `False` para los 47 items del nivel superior de Audio Effects -- incluido
"Audio Effect Rack", que tiene seis carpetas de racks del usuario adentro. `tieneHijos` es la
pregunta que la app necesita y se contesta aparte, desde 2.2.0.

## El mixer

`/digitaldeck/mixer/tracks` contesta con todos los canales del set de una vez. Una sola respuesta y
no una pregunta por canal: la ventana del mixer dibuja veinte faders juntos, y veinte idas y vueltas
son veinte chances de que un datagrama se pierda sin que nadie se entere.

Cada línea son siete campos separados por tabulación:

```
índice  nombre  clase  color  volumen  profundidad  lectura
```

* **índice** es lo que hay que mandar de vuelta para mover ese canal. Es posicional, así que borrar
  un track renumera todo lo que está a su derecha — por eso el roster viaja entero y no por partes.
* **clase** es `normal`, `group`, `return` o `master`.
* **volumen** es el 0..1 normalizado de Live, no decibeles.
* **profundidad** es cuántos grupos tiene arriba. Sin eso la ventana sólo puede dibujar una fila
  plana de faders, que para un set armado con grupos es peor foto que la del mixer de Live.
* **lectura** es lo que Live imprime abajo de su propio fader: `-6.02 dB`, `-inf dB`. **La calcula
  Live, no la app.** El recorrido del fader es lineal en el 0..1 —el fader de Live también, por eso
  la unidad cae cerca de 0.85— pero los decibeles detrás de esa posición siguen una curva que
  Ableton nunca publicó. Reimplementarla acá sería un número que parece autoridad y que se despega
  de lo que el usuario lee en la otra pantalla.

`/digitaldeck/mixer/subscribe 1` prende el push y contesta con el roster; `0` lo apaga. Mientras
está prendido el script tiene un listener por canal, y publica:

* `/live/track/get/volume índice valor lectura` cuando alguien mueve un fader adentro de Live.
* `/digitaldeck/mixer/tracks` de nuevo cuando aparece, desaparece o se renombra un track.

Se prende y se apaga a pedido justamente por eso: un set de cuarenta canales son cuarenta listeners,
y cada uno es un datagrama que la app no tiene dónde dibujar con la ventana cerrada.

### Los índices de los retornos

Los retornos viven en `song.return_tracks`, afuera de `song.tracks`, así que necesitaban lugar
propio en el espacio de índices. Se cuentan hacia abajo desde el master:

| Índice | Qué es |
|---|---|
| `0` y arriba | Los tracks del set, en orden |
| `-1` | El track seleccionado |
| `-2` | El master |
| `-3`, `-4`, … | El primer retorno, el segundo, … |

Contar hacia abajo y no reservar un rango arriba es lo que deja intacto todo índice que ya existía:
un botón guardado contra el track 3, o contra el master, no se entera de que los retornos llegaron.

## Estructura

```
DigitalDeck/            ← esto es lo que se copia a Ableton
├─ __init__.py          create_instance(), el punto de entrada que llama Live
├─ control_surface.py   ciclo de vida, tick, logging
├─ server.py            socket UDP no bloqueante
├─ osc.py               codec OSC 1.0 (sin dependencias)
├─ dispatch.py          ruteo de direcciones y coerción de argumentos
├─ context.py           acceso al LOM y resolución de tracks/escenas/slots
├─ observers.py         push de cambios de estado hacia la app
├─ version.py           versión del protocolo
└─ handlers/            la tabla de direcciones, por área
tests/                  se corren fuera de Live, con un LOM falso
```

### Dos reglas que explican casi todo el diseño

**El LOM sólo se toca desde el hilo de Live.** Llamarlo desde un worker no lanza una excepción:
cierra Live. Por eso no hay hilo de recepción — el socket es no bloqueante y se drena desde el
tick del control surface, que Live invoca en su propio hilo.

**Una excepción suelta mata el script.** Y recuperarlo significa reiniciar Live. Por eso el
dispatcher atrapa todo lo que tiran los handlers y lo devuelve como `/digitaldeck/error`, y por eso
`control_surface.py` está lleno de `try/except` alrededor de cosas que "no deberían" fallar.

## Tests

```bash
cd src/DigitalDeck.RemoteScript
python -m unittest discover -s tests
```

Sin dependencias: `unittest` de la stdlib, y un LOM falso en `tests/fakes.py` que se instala como
módulo `Live`. Cubren el codec, el ruteo y la tabla de direcciones entera.

Lo que estos tests **no** pueden probar es que Live realmente exponga los atributos que el script
usa — eso es comportamiento no contractual y sólo lo verifica la corrida de humo contra Ableton
real (`AbletonSmokeTests` en `tests/DigitalDeck.Integrations.Tests`).

## Compatibilidad

Escrito para **Live 12** (Python 3.11), sin sintaxis posterior a 3.8 para que siga cargando en
Live 11. La clase base del control surface se resuelve en runtime: primero `_Framework`, después
`ableton.v2` — así una versión futura que saque una de las dos cuesta una línea, no un script que
no carga.

Esto es una integración **no contractual**: Ableton no documenta ni promete nada de esto. Antes de
recomendar una versión nueva de Live hay que correr la suite de humo contra ella.
