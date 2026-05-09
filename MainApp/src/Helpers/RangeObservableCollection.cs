using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Albion_App.Helpers;

/// <summary>
/// <see cref="ObservableCollection{T}"/> con soporte para operaciones por lote
/// (<see cref="AddRange"/>, <see cref="ReplaceAll"/>) que disparan UNA sola
/// notificación <see cref="NotifyCollectionChangedAction.Reset"/> en lugar de
/// una por elemento.
///
/// <para>Por qué: con virtualización activada, ItemsControl puede tolerar miles
/// de adds individuales — pero antes de eso, cada Add dispara CollectionChanged,
/// PropertyChanged("Count"), e indexer-changed. Con 300 ítems eso son ~900
/// eventos en el hilo UI, suficientes para producir un pequeño jank.</para>
///
/// <para>Reset es la opción más compatible: ItemContainerGenerator lo trata como
/// "rebuild from scratch", lo que coincide con el caso de uso típico (búsqueda
/// que reemplaza todo el resultado anterior). El precio es que se pierde la
/// selección previa, pero en una búsqueda eso es lo que el usuario espera.</para>
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public RangeObservableCollection() { }

    public RangeObservableCollection(IEnumerable<T> collection) : base(collection) { }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suppressNotification) return;
        base.OnCollectionChanged(e);
    }

    /// <summary>
    /// Añade todos los elementos en bloque y dispara UN solo evento Reset.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _suppressNotification = true;
        try
        {
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseResetNotifications();
    }

    /// <summary>
    /// Reemplaza el contenido completo de la colección en bloque (Clear + Add)
    /// disparando UN solo evento Reset. Equivalente a <c>Clear() + AddRange()</c>
    /// pero sin las dos notificaciones intermedias.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseResetNotifications();
    }

    private void RaiseResetNotifications()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        // "Item[]" es el nombre canónico del indexer en ObservableCollection;
        // necesario para que las suscripciones a propiedades indexadas (raras
        // pero existentes) se enteren del cambio.
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
