using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Decho.ViewModels;

namespace Decho.Views;

public partial class MessageComposerView : UserControl
{
    public MessageComposerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    private async void OnFileUploadClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessageComposerViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select a file to upload",
        });

        var file = files?.FirstOrDefault();
        if (file?.TryGetLocalPath() is string path)
        {
            vm.RequestFileUpload(path);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (e.Data.Contains(DataFormats.Files))
#pragma warning restore CS0618
            e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MessageComposerViewModel vm) return;

#pragma warning disable CS0618
        var paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p is not null);
#pragma warning restore CS0618

        if (paths is string path)
            vm.RequestFileUpload(path);
    }
}
