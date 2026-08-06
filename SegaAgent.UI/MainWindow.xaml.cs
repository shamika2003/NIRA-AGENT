/*
 * filename: MainWindow.xaml.cs
 */

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SegaAgent.UI.ViewModels;

namespace SegaAgent.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.Messages.CollectionChanged +=
            Messages_CollectionChanged;

        foreach (var message in viewModel.Messages)
        {
            message.PropertyChanged += Message_PropertyChanged;
        }

        Loaded += (_, _) =>
        {
            MessageInput.Focus();
        };
    }


    private void Messages_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ChatMessageViewModel message in e.NewItems)
            {
                message.PropertyChanged += Message_PropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (ChatMessageViewModel message in e.OldItems)
            {
                message.PropertyChanged -= Message_PropertyChanged;
            }
        }

        ScrollChatToBottom();
    }


    private void Message_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(ChatMessageViewModel.Content))
        {
            ScrollChatToBottom();
        }
    }


    private void ScrollChatToBottom()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ChatScroll.ScrollToEnd();
            }),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }


    // ==========================================
    // TITLE BAR DRAG
    // ==========================================

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }


    // ==========================================
    // MINIMIZE
    // ==========================================

    private void MinimizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }


    // ==========================================
    // MAXIMIZE
    // ==========================================

    private void MaximizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ToggleMaximize();
    }


    private void ToggleMaximize()
    {
        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }


    // ==========================================
    // CLOSE
    // ==========================================

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Hide();
    }


    private void MessageInput_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        // Enter = Send
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;

            if (DataContext is MainWindowViewModel viewModel &&
                viewModel.SendCommand.CanExecute(null))
            {
                viewModel.SendCommand.Execute(null);
            }

            return;
        }

        // Shift + Enter = New Line
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.Shift)
        {
            // Let TextBox handle the newline normally.
            e.Handled = false;
        }
    }
}