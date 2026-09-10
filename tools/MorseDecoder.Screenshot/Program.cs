using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MorseDecoder.App;

namespace MorseDecoder.Screenshot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: MorseDecoder.Screenshot <output.png>");
            return 1;
        }

        var outputPath = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var application = new App();
        application.InitializeComponent();
        application.StartupUri = null;

        var window = new MainWindow
        {
            Width = 1280,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false
        };

        window.Show();

        var clickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        clickTimer.Tick += (_, _) =>
        {
            clickTimer.Stop();
            var testButton = FindVisualChildren<Button>(window)
                .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "测试信号", StringComparison.Ordinal));
            testButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        clickTimer.Start();

        var captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(11)
        };
        captureTimer.Tick += (_, _) =>
        {
            captureTimer.Stop();

            try
            {
                window.UpdateLayout();
                CaptureWindow(window, outputPath);
                Console.WriteLine($"Screenshot saved: {outputPath}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }

            window.Close();
            application.Shutdown();
        };
        captureTimer.Start();

        application.Run();
        return 0;
    }

    private static void CaptureWindow(Window window, string outputPath)
    {
        var content = window.Content as FrameworkElement ?? window;
        var width = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
        var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

