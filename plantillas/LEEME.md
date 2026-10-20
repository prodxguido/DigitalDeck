# Plantillas

Estanterías ya armadas para importar. Cada archivo `.deckpack` trae un grupo con sus botones
adentro, y al importarlo elegís en qué se convierte. Podés meter las nueve en un mismo perfil.

**No hace falta venir hasta acá.** Estas plantillas viajan adentro de la app: desde el menú ☰,
**Instalar plantillas…** abre la lista completa con lo que trae cada una, podés marcar varias de
una vez y ahí mismo decidir cómo entran. Esta carpeta queda para mirarlas en un editor de texto o
para pasarle un `.deckpack` suelto a alguien.

Las dos formas, tanto al instalar como al importar un archivo:

- **Como una página nueva** — lo que viene marcado, y lo normal para una marca entera. Se suma una
  pestaña con los botones ya desplegados: el grupo que envuelve la plantilla se abre y su nombre
  pasa a la pestaña, así que no quedan quince plugins atrás de una tecla. La página llega con la
  grilla con la que fue armada.
- **Como un grupo en la página que estoy usando** — cae en la página abierta (o dentro del grupo
  en el que estés parado, si entraste a uno) y ocupa la primera celda libre. Sirve para armar una
  sola página con las tres marcas que usás de verdad. Si la página está llena, te lo dice por el
  nombre del grupo en vez de dejar botones en un lugar donde no se ven.

En los dos casos no se mueve nada de lo que ya tenías, y dos plantillas con el mismo nombre no se
pisan: la segunda pestaña entra como «Waves (2)».

Los importadores de archivo están en el menú ☰:

- **Importar paquetes…** — abre uno o varios `.deckpack` y hace la misma pregunta. En el clic
  derecho sobre una celda vacía está **Importar paquetes acá**, que no pregunta nada porque
  «acá» ya es la respuesta.
- **Importar paquete como perfil** — para un respaldo, que es un deck entero y merece llegar como
  tal.

## Qué hay

| Archivo | Qué trae |
|---|---|
| `tareas-basicas.deckpack` | Lo de todos los días, sin Ableton de por medio: YouTube, Gmail y Drive en el navegador, más las carpetas Documentos y Descargas. |
| `fabfilter.deckpack` | Los 15 plugins de FabFilter, agrupados por para qué sirven: EQ, dinámicas, saturación, reverb, delay, filtros y sintetizadores. |
| `waves.deckpack` | 85 plugins de Waves, en 10 carpetas por categoría: EQ, Renaissance, compresores, mastering, reverb, delay y modulación, saturación, voz, limpieza y utilidad. |
| `valhalla.deckpack` | Los 9 de Valhalla, planos: los cinco reverbs, los dos delays y los dos moduladores. |
| `u-he.deckpack` | 32 de u-he en 3 carpetas: sintes, efectos y la serie Uhbik. |
| `soundtoys.deckpack` | 21 de Soundtoys en 5 carpetas: saturación, delay, modulación, la serie Little y el Effect Rack. |
| `roland-cloud.deckpack` | 24 de Roland Cloud en 3 carpetas: sintes clásicos, cajas de ritmo y la línea ZEN. |
| `izotope.deckpack` | 44 de iZotope en 5 carpetas: mastering (Ozone 11 y sus módulos), mezcla (Neutron 4), voz, reparación (RX 11) y creativos. |
| `d16.deckpack` | 19 de D16 en 5 carpetas: sintes y ritmo, saturación, modulación, filtros y tiempo. |
| `ableton-dispositivos.deckpack` | Los 62 dispositivos de fábrica de Live en 9 carpetas: EQ y filtros, dinámica, saturación, reverb y delay, modulación, utilidad, efectos MIDI, los MIDI nuevos de Live 12 y los Max for Live (LFO, Shaper, Envelope Follower). Sin presets: sólo los dispositivos. |

`ableton-dispositivos` es el único que no depende de tener ningún plugin comprado: apunta a lo que
Live trae de fábrica, así que anda en cualquier instalación. La contra es que los nombres de fábrica
cambiaron entre versiones —Live 12 renombró Chorus a «Chorus-Ensemble», Flanger y Phaser a
«Phaser-Flanger» y Frequency Shifter a «Shifter»— y la plantilla usa los de Live 12. En Live 10 u 11
esos cuatro botones hay que reelegirlos del picker; el resto coincide.

`tareas-basicas` es el único que no necesita Ableton. Sus dos botones de carpeta apuntan a
`%USERPROFILE%\Documents` y `%USERPROFILE%\Downloads`, no a una ruta escrita a mano: el mismo
archivo funciona en cualquier máquina y con cualquier nombre de usuario.

## Los íconos

Cada botón trae un PNG propio: un cuadrado redondeado del color de la familia con el nombre del
plugin, ajustado para que se lea a tamaño de tile. **No son logos de marca** — son generados, así
que la plantilla se reparte sin problema de derechos.

Están dentro de cada `.deckpack` (carpeta `icons/`) y también sueltos en `plantillas/iconos/<marca>/`
para verlos o retocarlos. Si cambiás algo en un manifest y querés regenerarlos:

```powershell
python herramientas/generar-iconos-plantillas.py            # todas
python herramientas/generar-iconos-plantillas.py waves.deckpack   # una sola
```

El script toma el `label` y el `color_hex` de cada botón del manifest, dibuja el PNG, lo mete en el
zip y le agrega el campo `icon_file`. Si preferís un logo de verdad para algún botón, cambiale el
PNG en `plantillas/iconos/<marca>/` con el mismo nombre y volvé a empaquetar (o editalo desde la
app con «Usar una imagen…» después de importar).

## Cómo funcionan

Los botones **no guardan la ruta del plugin**. Guardan el nombre y la sección del browser de Live
—`plugins` en las de marcas, y `audio_effects`, `midi_effects` o `max_for_live` en la de
dispositivos de fábrica—, y al apretarlos le piden a Live que cargue eso — la única forma que hay, porque un
Remote Script no puede cargar un `.vst3` de disco. Dos consecuencias, las dos buenas:

- **No hay nada que configurar.** Si el plugin está instalado y Live lo escaneó, el botón anda tal
  cual viene, sin importar en qué disco esté instalado.
- **La plantilla no se rompe** al cambiar de máquina, ni al reinstalar, ni entre Windows y Mac.

Lo que sí puede pasar: que el nombre no coincida. Live lista cada plugin con el nombre que trae el
binario, y ahí se cuela todo — la versión ("FabFilter Pro-Q 3" y "Pro-Q 4" son dos nombres
distintos) y, en Waves, el formato de canal: **cada plugin aparece como "Mono" y como "Stereo" por
separado**, así que el nombre real es "CLA-76 Stereo", no "CLA-76". Estas plantillas usan las
variantes Stereo, que es lo que va en una pista de Ableton.

## Cuáles están verificadas y cuáles no

Esto importa más que cualquier otra cosa de este archivo.

- `fabfilter` y `waves` **están verificadas contra un Live real** (12.3, Waves 14) con el mismo
  mecanismo que usa el picker: los 100 nombres existen y ninguno es ambiguo.
- Las otras siete **no**. Están escritas con los nombres que esos bundles usan habitualmente, pero
  nadie las contrastó todavía contra una instalación de verdad. Donde hay versiones conviviendo
  —Ozone 11 y no 10, RX 11, Neutron 4, Nectar 4, Phoscyon 2 y no 1— se eligió la actual, y ese es
  justamente el campo que más fácil no coincide.

En `ableton-dispositivos`, lo que más chance tiene de no coincidir es la carpeta **LFO y Max for
Live**: esos siete están buscados en la sección `max_for_live`, y según la versión Live los lista
ahí, entre los efectos de audio, o no los trae instalados. Si alguno falla, reelegilo del picker.

Para verificarlas, con Live abierto y el script instalado:

```powershell
$env:DIGITALDECK_LIVE = "1"
dotnet test --filter FullyQualifiedName~TemplateNames --logger "console;verbosity=detailed"
```

Busca cada nombre de cada plantilla en la sección del browser que ese botón declara e imprime cuáles no aparecen y qué es lo
más parecido que encontró, que suele decir sola cuál es la corrección:

```
izotope.deckpack        41/44 encontrados
    falta: Ozone 11 Maximizer              lo más parecido: Ozone 10 Maximizer
```

No falla cuando falta un plugin, porque un bundle que no está instalado en esta máquina no es un
error de la plantilla; es un informe para leer. Un bundle entero en cero quiere decir que no lo
tenés, y no hay nada que arreglar.

Un botón cuyo nombre no existe en ese Live falla al apretarlo, con el mensaje del script diciendo
que no encontró nada indexado, y se arregla desde el editor eligiendo el plugin de la lista real.

## Armar una nueva

La forma corta es desde la app: armá el grupo como lo quieras, clic derecho sobre él y
**Exportar grupo**. Eso escribe un `.deckpack` listo para pasar.

A mano también se puede: un `.deckpack` es un zip con un solo `manifest.json` adentro (más una
carpeta `icons/` si hay imágenes). Se descomprime, se edita el JSON y se vuelve a comprimir.

Al agregar una plantilla acá, sumala a la tabla de arriba y al `[InlineData]` de
`tests/DigitalDeck.Tests/TemplatePackTests.cs`, que la importa de verdad y avisa si le falta un
campo, tiene dos botones en la misma celda, repite un plugin o se le fue alguno afuera de la
grilla. La verificación contra Live de más arriba toma sola cualquier `.deckpack` que aparezca en
esta carpeta, así que esa no hay que tocarla.

## Un límite a tener en cuenta

Una carpeta muestra una página de 6×4, o sea **24 botones**. FabFilter entra cómodo con 15. Waves
no entra ni cerca, y por eso viene partida: el grupo `WAVES` tiene diez carpetas de categoría
adentro, y la más llena — compresores — tiene diez. El anidado no tiene límite de profundidad, así
que ese es el patrón para cualquier marca grande hasta que exista la paginación dentro de carpetas.
