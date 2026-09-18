using System.Windows;
using PdfRagQa.Client.ViewModels;

namespace PdfRagQa.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}