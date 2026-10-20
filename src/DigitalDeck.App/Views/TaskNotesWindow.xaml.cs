using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DigitalDeck.App.Views;

public sealed record WorkflowTask(string Title, string Description, string Priority, IReadOnlyList<string> Steps);

public partial class TaskNotesWindow : Window
{
    private readonly IReadOnlyList<WorkflowTask> _tasks = new[]
    {
        new WorkflowTask("Revisar mezcla de Beat Square", "Comprobar niveles, dinámica y traducción antes de exportar.", "Urgente", new[] { "Abrir el proyecto desde la lista de Ableton.", "Revisar ganancia del master y picos de cada grupo.", "Escuchar en monitores y auriculares.", "Guardar una versión nueva y exportar la mezcla." }),
        new WorkflowTask("Exportar mezcla principal", "Preparar el bounce estéreo para revisión.", "Urgente", new[] { "Comprobar el inicio y final del arreglo.", "Elegir WAV 24-bit y la frecuencia del proyecto.", "Exportar y escuchar el archivo completo." }),
        new WorkflowTask("Revisar clipping del master", "Asegurar margen antes de compartir el beat.", "Urgente", new[] { "Mirar los picos del master.", "Bajar la ganancia si hay clipping.", "Reproducir la parte más cargada." }),
        new WorkflowTask("Guardar versión de seguridad", "Crear una versión recuperable del trabajo actual.", "Urgente", new[] { "Guardar el proyecto.", "Duplicar el .als con fecha.", "Confirmar que abre correctamente." }),
        new WorkflowTask("Preparar envío al cliente", "Dejar lista la carpeta que se va a compartir.", "Urgente", new[] { "Ejecutar Collect All and Save.", "Revisar samples faltantes.", "Usar el botón Compartir del proyecto." }),
        new WorkflowTask("Comprobar referencias", "Comparar el balance con una referencia confiable.", "Urgente", new[] { "Importar la referencia a una pista aparte.", "Igualar niveles de escucha.", "Anotar diferencias de graves y dinámica." }),
        new WorkflowTask("Organizar samples recopilados", "Verificar que el proyecto tenga todos los audios dentro de su carpeta.", "Media", new[] { "Abrir File > Manage Files en Ableton.", "Ejecutar Collect All and Save.", "Revisar la carpeta Samples del proyecto.", "Guardar y volver a compartir si corresponde." }),
        new WorkflowTask("Ordenar pistas y grupos", "Dejar el proyecto preparado para continuar trabajando.", "Media", new[] { "Nombrar pistas y grupos.", "Colorear las secciones.", "Guardar el proyecto." }),
        new WorkflowTask("Revisar automatizaciones", "Confirmar que los cambios importantes estén guardados.", "Media", new[] { "Recorrer la canción completa.", "Revisar filtros, volumen y envíos.", "Guardar una nueva versión." }),
        new WorkflowTask("Preparar presets para producción", "Dejar ordenados los instrumentos y presets del perfil Producción.", "Media", new[] { "Abrir la página Producción.", "Comprobar ZENOLOGY, Hive, Pigments y Analog Lab.", "Eliminar duplicados y marcar favoritos.", "Guardar los cambios del perfil." }),
        new WorkflowTask("Limpiar backups antiguos", "Reducir copias que ya no sean necesarias sin tocar el proyecto actual.", "Baja", new[] { "Abrir la carpeta del proyecto.", "Conservar la última copia estable.", "Mover backups antiguos a un archivo externo.", "Verificar que el .als principal siga abriendo." }),
        new WorkflowTask("Actualizar nombres de presets", "Mantener ordenada la librería del perfil.", "Baja", new[] { "Revisar los presets usados.", "Usar nombres consistentes.", "Guardar la plantilla." })
    };

    public TaskNotesWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        UrgentList.ItemsSource = _tasks.Where(t => t.Priority == "Urgente").ToArray();
        MediumList.ItemsSource = _tasks.Where(t => t.Priority == "Media").ToArray();
        LowList.ItemsSource = _tasks.Where(t => t.Priority == "Baja").ToArray();
        SetItemTemplate(UrgentList); SetItemTemplate(MediumList); SetItemTemplate(LowList);
    }

    private static void SetItemTemplate(ListBox list)
    {
        list.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        var border = list.ItemTemplate.VisualTree;
        border.SetValue(Border.PaddingProperty, new Thickness(9, 7, 9, 7));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 5));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(24, 32, 31)));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.FontSizeProperty, 11.0);
        border.AppendChild(text);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(WidthProperty, 108.0));
        style.Setters.Add(new Setter(HeightProperty, 44.0));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 5, 5)));
        list.ItemContainerStyle = style;
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
    }

    private void Task_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not WorkflowTask task) return;
        DetailTitle.Text = task.Title;
        DetailDescription.Text = task.Description;
        StepsList.ItemsSource = task.Steps.Select((step, i) => $"{i + 1}. {step}").ToArray();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
