using System.Windows;

namespace Qingying;

public partial class MediaInfoWindow : Window
{
    public MediaInfoWindow(IReadOnlyList<MediaFact> facts)
    {
        InitializeComponent();
        FactList.ItemsSource = facts;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
