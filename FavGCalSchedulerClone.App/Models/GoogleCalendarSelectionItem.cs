using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FavGCalSchedulerClone.App.Models;

public sealed class GoogleCalendarSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Id { get; init; } = "";
    public string Summary { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
