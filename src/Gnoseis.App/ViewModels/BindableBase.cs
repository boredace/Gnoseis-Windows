/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Gnoseis.Core.Data;
using Microsoft.UI.Dispatching;

namespace Gnoseis.App.ViewModels;

public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Base of page view models. While active (the page is shown) it listens for data changes and
/// reloads, which replaces the Room Flow observers of the Android app.
/// </summary>
public abstract class PageViewModel : BindableBase
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _isActive;
    private bool _isLoading;

    protected static AppContainer Data => App.Container;

    public bool IsLoading
    {
        get => _isLoading;
        protected set => SetProperty(ref _isLoading, value);
    }

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }
        _isActive = true;
        Data.Database.DataChanged += OnDataChangedAnyThread;
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }
        _isActive = false;
        Data.Database.DataChanged -= OnDataChangedAnyThread;
    }

    /// <summary>Called on the UI thread after data has changed.</summary>
    protected virtual void OnDataChanged(DataChangedEventArgs e)
    {
    }

    private void OnDataChangedAnyThread(object? sender, DataChangedEventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (_isActive)
            {
                OnDataChanged(e);
            }
        });
}
