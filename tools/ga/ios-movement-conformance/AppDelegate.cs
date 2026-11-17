using System.Diagnostics.CodeAnalysis;
using Foundation;
using UIKit;

[Register("SkecsGaMovementAppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    internal static string[] LaunchArguments { get; private set; } = [];

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SceneDelegate))]
    private static void Main(string[] args)
    {
        LaunchArguments = args;
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
