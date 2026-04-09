using Android.App;
using Android.Content.PM;
using Android.OS;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Hosts the .NET MAUI application on Android.
    /// </summary>
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize
            | ConfigChanges.Orientation
            | ConfigChanges.UiMode
            | ConfigChanges.ScreenLayout
            | ConfigChanges.SmallestScreenSize
            | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}
