using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Albion_App.Behaviors;

public static class ProgressiveLoading
{
    // ─── Attached Properties ────────────────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ProgressiveLoading),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(IEnumerable),
            typeof(ProgressiveLoading),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty BatchSizeProperty =
        DependencyProperty.RegisterAttached(
            "BatchSize",
            typeof(int),
            typeof(ProgressiveLoading),
            new PropertyMetadata(20));

    public static readonly DependencyProperty DelayMsProperty =
        DependencyProperty.RegisterAttached(
            "DelayMs",
            typeof(int),
            typeof(ProgressiveLoading),
            new PropertyMetadata(1));

    // Estado interno por control
    private static readonly DependencyProperty CancellationProperty =
        DependencyProperty.RegisterAttached(
            "Cancellation",
            typeof(CancellationTokenSource),
            typeof(ProgressiveLoading));

    public static readonly DependencyProperty
        ClearOnUnloadProperty =
            DependencyProperty.RegisterAttached(
                "ClearOnUnload",
                typeof(bool),
                typeof(ProgressiveLoading),
                new PropertyMetadata(false));

    public static bool GetClearOnUnload(
        DependencyObject obj)
        => (bool)obj.GetValue(
            ClearOnUnloadProperty);

    public static void SetClearOnUnload(
        DependencyObject obj,
        bool value)
        => obj.SetValue(
            ClearOnUnloadProperty,
            value);

    private sealed class LoadingState
    {
        public ObservableCollection<object> Proxy { get; } = [];
        public int CurrentIndex { get; set; }
    }

    private static readonly DependencyProperty
        StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(LoadingState),
                typeof(ProgressiveLoading));
    // ─── Get / Set ───────────────────────────────────────────────────────────

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    public static IEnumerable? GetSource(DependencyObject obj)
        => (IEnumerable?)obj.GetValue(SourceProperty);

    public static void SetSource(DependencyObject obj, IEnumerable? value)
        => obj.SetValue(SourceProperty, value);

    public static int GetBatchSize(DependencyObject obj)
        => (int)obj.GetValue(BatchSizeProperty);

    public static void SetBatchSize(DependencyObject obj, int value)
        => obj.SetValue(BatchSizeProperty, value);

    public static int GetDelayMs(DependencyObject obj)
        => (int)obj.GetValue(DelayMsProperty);

    public static void SetDelayMs(DependencyObject obj, int value)
        => obj.SetValue(DelayMsProperty, value);

    // ─── Eventos ─────────────────────────────────────────────────────────────

    private static void OnIsEnabledChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl itemsControl)
            return;

        if ((bool)e.NewValue)
        {
            itemsControl.Loaded += OnLoaded;
            itemsControl.Unloaded += OnUnloaded;

            StartLoading(itemsControl);
        }
        else
        {
            itemsControl.Loaded -= OnLoaded;
            itemsControl.Unloaded -= OnUnloaded;

            Cleanup(itemsControl);
        }
    }
    
    private static void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
            return;

        if (!GetIsEnabled(itemsControl))
            return;

        StartLoading(itemsControl);
    }

    private static void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
            return;

        Cleanup(itemsControl);
    }

    

    private static void OnSourceChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl itemsControl)
            return;

        if (GetIsEnabled(itemsControl))
            StartLoading(itemsControl);
    }

    // ─── Core ────────────────────────────────────────────────────────────────


    private static async void StartLoading(ItemsControl itemsControl)
    {
        // ─── Cancelar carga anterior ─────────────────────────────

        if (itemsControl.GetValue(CancellationProperty)
            is CancellationTokenSource oldCts)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();

        itemsControl.SetValue(
            CancellationProperty,
            cts);

        var ct = cts.Token;

        try
        {
            var source = GetSource(itemsControl);

            if (source is null)
                return;

            var batchSize =
                Math.Max(1,
                    GetBatchSize(itemsControl));

            var delayMs =
                Math.Max(0,
                    GetDelayMs(itemsControl));

            // ─── Obtener o crear estado ──────────────────────────

            var state =
                itemsControl.GetValue(StateProperty)
                    as LoadingState;

            if (state is null)
            {
                state = new LoadingState();

                itemsControl.SetValue(
                    StateProperty,
                    state);

                itemsControl.ItemsSource =
                    state.Proxy;
            }

            // Si el proxy fue removido
            if (!ReferenceEquals(
                    itemsControl.ItemsSource,
                    state.Proxy))
            {
                itemsControl.ItemsSource =
                    state.Proxy;
            }

            // Snapshot del source
            var sourceList =
                source.Cast<object>()
                    .ToList();

            // Ya terminó antes
            if (state.CurrentIndex >= sourceList.Count)
                return;

            // Esperar primer render
            await itemsControl.Dispatcher
                .InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);

            var buffer =
                new List<object>(batchSize);

            // ─── Reanudar desde índice actual ────────────────────

            for (var i = state.CurrentIndex;
                 i < sourceList.Count;
                 i++)
            {
                ct.ThrowIfCancellationRequested();

                buffer.Add(sourceList[i]);

                state.CurrentIndex = i + 1;

                if (buffer.Count < batchSize)
                    continue;

                foreach (var item in buffer)
                    state.Proxy.Add(item);

                buffer.Clear();

                // Respirar UI thread
                await Dispatcher.Yield(DispatcherPriority.Render);

                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }

            // Flush final
            foreach (var item in buffer)
                state.Proxy.Add(item);
        }
        catch (OperationCanceledException)
        {
            // esperado
        }
    }
    
    private static void Cleanup(
        ItemsControl itemsControl)
    {
        // ─── Cancelar carga ─────────────────────────────────────

        if (itemsControl.GetValue(CancellationProperty)
            is CancellationTokenSource cts)
        {
            cts.Cancel();
            cts.Dispose();
        }

        itemsControl.ClearValue(
            CancellationProperty);

        // ─── Limpieza opcional ──────────────────────────────────

        if (!GetClearOnUnload(itemsControl))
            return;

        if (itemsControl.GetValue(StateProperty)
            is not LoadingState state)
            return;

        state.Proxy.Clear();
        state.CurrentIndex = 0;
    }
}