using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.Input;
using Hardcodet.Wpf.TaskbarNotification;

namespace DriveCaffeine;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly TaskbarIcon icon;
    public RefreshInterval RefreshInterval { get; set; } = RefreshInterval.s3;
    public readonly List<string> EnabledDrives = [];
    private readonly List<CaffineTask> activeTask = [];

    public MainWindow()
    {
        InitializeComponent();
        Application.Current.Exit += CurrentOnExit;

        var uri = new Uri("pack://application:,,,/DriveCaffeine;component/coffee-beans.ico", UriKind.Absolute);
        using var iconStream = Application.GetResourceStream(uri)!.Stream;
        icon = new TaskbarIcon
        {
            MenuActivation = PopupActivationMode.LeftOrRightClick,
            Visibility = Visibility.Visible,
            Icon = new Icon(iconStream),
            ContextMenu = new TrayMenu(this)
        };
    }

    private void CurrentOnExit(object sender, ExitEventArgs e)
    {
        icon.Dispose();
    }

    [RelayCommand]
    public void ToggleDrive(string driveRoot)
    {
        if (EnabledDrives.Contains(driveRoot))
        {
            var task = activeTask.First(a => a.DriveRoot == driveRoot);
            task.CancelToken.Cancel();
            task.CancelToken.Dispose();
            activeTask.Remove(task);
            EnabledDrives.Remove(driveRoot);
        }
        else
        {
            EnabledDrives.Add(driveRoot);
            activeTask.Add(new CaffineTask(IntervalToTimeSpan(RefreshInterval), driveRoot));
        }
    }

    [RelayCommand]
    public void SetInterval(RefreshInterval interval)
    {
        foreach (var task in activeTask)
        {
            task.Delay = IntervalToTimeSpan(interval);
        }

        RefreshInterval = interval;
    }

    private TimeSpan IntervalToTimeSpan(RefreshInterval interval)
    {
        return interval switch
        {
            RefreshInterval.s1 => TimeSpan.FromMinutes(1),
            RefreshInterval.s3 => TimeSpan.FromMinutes(3),
            RefreshInterval.s5 => TimeSpan.FromMinutes(5),
            RefreshInterval.s10 => TimeSpan.FromMinutes(10),
            _ => throw new Exception("uknown interval")
        };
    }
}

public enum RefreshInterval
{
    s1,
    s3,
    s5,
    s10
}

public class TrayMenu : ContextMenu
{
    private readonly MainWindow window;

    static TrayMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TrayMenu), new FrameworkPropertyMetadata(typeof(ContextMenu)));
    }

    public TrayMenu(MainWindow window)
    {
        this.window = window;
        Opened += OnOpened;
    }

    private void OnOpened(object sender, RoutedEventArgs e)
    {
        Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            var root = drive.RootDirectory.Root.Name;
            var subName = drive.IsReady ? drive.VolumeLabel : string.Empty;
            Items.Add(new MenuItem
            {
                Header = $"{root} ({subName})",
                IsChecked = window.EnabledDrives.Contains(root),
                Command = window.ToggleDriveCommand,
                CommandParameter = root
            });
        }

        Items.Add(new Separator());
        foreach (RefreshInterval interval in  Enum.GetValues(typeof(RefreshInterval)))
        {
            Items.Add(new MenuItem
            {
                Header = $"{interval.ToString().TrimStart('s')} minutes",
                IsChecked = window.RefreshInterval == interval,
                Command = window.SetIntervalCommand,
                CommandParameter = interval
            });
        }

        Items.Add(new Separator());
        Items.Add(new MenuItem
        {
            Header = "Exit",
            Command = new RelayCommand(() => Application.Current.Shutdown(0))
        });
    }
}

public class CaffineTask
{
    public CancellationTokenSource CancelToken { get; }
    public TimeSpan Delay { get; set; }
    public string DriveRoot { get; set; }

    public CaffineTask(TimeSpan delay, string driveRoot)
    {
        DriveRoot = driveRoot;
        Delay = delay;
        CancelToken = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (!CancelToken.IsCancellationRequested)
            {
                if (!Directory.Exists(DriveRoot))
                {
                    await Task.Delay(delay);
                    continue;
                }

                try
                {
                    var intervalId = Guid.NewGuid().ToString();
                    var path = System.IO.Path.Combine(DriveRoot, intervalId + ".caffeine");
                    File.WriteAllText(path, intervalId);
                    File.Delete(path);
                    await Task.Delay(delay);
                }
                catch (Exception e) when (!Debugger.IsAttached)
                {

                }
            }
        });
    }
}
