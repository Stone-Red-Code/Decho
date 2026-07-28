using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.SpellChecker;

using Decho.Models;
using Decho.ViewModels;

using System.Diagnostics;
using System.Globalization;
using System.Reactive;

namespace Decho.Views;

public partial class MessageComposerView : UserControl
{
    private readonly TextBoxSpellChecker _spellChecker;

    public MessageComposerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        DataContextChanged += OnDataContextChanged;

        // Intercept Enter before the TextBox processes it (AcceptsReturn=True would insert a newline)
        MessageTextBox.AddHandler(InputElement.KeyDownEvent, OnTextBoxTunnelingKeyDown, RoutingStrategies.Tunnel);

        _spellChecker = new TextBoxSpellChecker(SpellCheckerConfig.Create("en_GB"));
        _spellChecker.Initialize(MessageTextBox);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MessageComposerViewModel vm)
        {
            vm.Autocomplete.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(AutocompleteController.ShowPopup) or nameof(AutocompleteController.FilterText))
                {
                    PositionPopupAtCursor();
                }
            };
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null)
        {
            return;
        }

        if (vm.Autocomplete.ShowPopup)
        {
            if (e.Key == Key.Down)
            {
                vm.Autocomplete.SelectedIndex = Math.Min(vm.Autocomplete.SelectedIndex + 1, vm.Autocomplete.FilteredItems.Count - 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                vm.Autocomplete.SelectedIndex = Math.Max(vm.Autocomplete.SelectedIndex - 1, 0);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Tab)
            {
                if (vm.Autocomplete.SelectedIndex >= 0 && vm.Autocomplete.SelectedIndex < vm.Autocomplete.FilteredItems.Count)
                {
                    InsertAutocomplete(vm.Autocomplete.FilteredItems[vm.Autocomplete.SelectedIndex]);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                vm.Autocomplete.ShowPopup = false;
                e.Handled = true;
                return;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            bool hasReply = vm.HasReplyTarget;
            bool hasFiles = vm.HasStagedFiles;
            if (hasReply || hasFiles)
            {
                if (hasReply) vm.ClearReplyTarget();
                if (hasFiles) vm.ClearStagedFiles();
                e.Handled = true;
                return;
            }
        }
    }

    private void OnTextBoxTunnelingKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null) return;

        // Don't intercept Enter when autocomplete is open (bubbling handler handles it)
        if (vm.Autocomplete.ShowPopup) return;

        vm.Draft = MessageTextBox.Text ?? "";
        vm.Send();
        e.Handled = true;
    }

    private void OnMentionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.DataContext is string item)
        {
            InsertAutocomplete(item);
        }
    }

    private void InsertAutocomplete(string item)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null)
        {
            return;
        }

        vm.InsertAutocomplete(item);
        MessageTextBox.CaretIndex = MessageTextBox.Text?.Length ?? 0;
        _ = MessageTextBox.Focus();
    }

    private void PositionPopupAtCursor()
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null)
        {
            return;
        }

        string text = MessageTextBox.Text ?? "";
        int targetIndex = Math.Min(vm.Autocomplete.TriggerCharIndex, text.Length);
        string beforeTarget = text[..targetIndex];

        double width = 0;
        if (beforeTarget.Length > 0)
        {
            FormattedText formatted = new(
                beforeTarget,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(MessageTextBox.FontFamily, MessageTextBox.FontStyle, MessageTextBox.FontWeight, MessageTextBox.FontStretch),
                MessageTextBox.FontSize,
                null);
            width = formatted.Width;
        }

        double maxOffset = Math.Max(0, MessageTextBox.Bounds.Width - 20);
        MentionPopup.HorizontalOffset = Math.Min(width + 4, maxOffset);
    }

    private async void OnFileUploadClicked(object? sender, RoutedEventArgs e)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select files to attach",
        });

        List<string> paths = [];
        foreach (IStorageFile file in files)
        {
            if (file.TryGetLocalPath() is string path)
            {
                paths.Add(path);
            }
        }

        if (paths.Count > 0)
        {
            vm.StageFiles(paths);
        }
    }

    private void OnRemoveStagedClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is StagedFile file)
        {
            MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
            vm?.RemoveStagedFile(file);
        }
    }

    private void OnClearStagedClick(object? sender, RoutedEventArgs e)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        vm?.ClearStagedFiles();
    }

    private void OnCancelReplyClick(object? sender, RoutedEventArgs e)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        vm?.ClearReplyTarget();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (e.Data.Contains(DataFormats.Files))
        {
#pragma warning restore CS0618
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        MessageComposerViewModel? vm = this.GetDataContext<MessageComposerViewModel>();
        if (vm is null)
        {
            return;
        }

#pragma warning disable CS0618
        List<string> paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList() ?? [];
#pragma warning restore CS0618

        if (paths.Count > 0)
        {
            vm.StageFiles(paths);
        }
    }
}