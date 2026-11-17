using Foundation;
using UIKit;

[Register("SkecsGaMovementSceneDelegate")]
public sealed class SceneDelegate : UIWindowSceneDelegate
{
    private CancellationTokenSource? _lifetime;
    private MovementSession? _session;
    private UITextView? _output;

    public override UIWindow? Window { get; set; }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene || Window is not null)
            return;

        var controller = new UIViewController();
        controller.View!.BackgroundColor = UIColor.SystemBackground;
        var output = new UITextView
        {
            Text = "SKECS GA Movement",
            Editable = false,
            Font = UIFont.GetMonospacedSystemFont(13, UIFontWeight.Regular),
            TextColor = UIColor.Label,
            BackgroundColor = UIColor.SystemBackground,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        controller.View.AddSubview(output);
        var safeArea = controller.View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints([
            output.TopAnchor.ConstraintEqualTo(safeArea.TopAnchor, 12),
            output.BottomAnchor.ConstraintEqualTo(safeArea.BottomAnchor, -12),
            output.LeadingAnchor.ConstraintEqualTo(safeArea.LeadingAnchor, 12),
            output.TrailingAnchor.ConstraintEqualTo(safeArea.TrailingAnchor, -12)
        ]);

        _output = output;
        Window = new UIWindow(windowScene) { RootViewController = controller };
        Window.MakeKeyAndVisible();

        string mode, profile;
        try
        {
            mode = ReadOption("--mode", "auto");
            profile = ReadOption("--profile", "short");
        }
        catch (ArgumentException failure)
        {
            Console.Error.WriteLine(failure.Message);
            Append($"Failed: {failure.Message}");
            return;
        }

        var lifetime = new CancellationTokenSource();
        var cancellation = lifetime.Token;
        var bundle = NSBundle.MainBundle.BundlePath;
        void ReportRun(string message) => Report(message, output, cancellation);
        var movement = new MovementSession(
            Path.Combine(bundle, "bundle"),
            Path.Combine(bundle, "Frameworks", "ga-metal-native.framework", "ga-metal-native"),
            ReportRun);
        _lifetime = lifetime;
        _session = movement;
        Append($"Running {mode} / {profile}");
        _ = RunSessionAsync(movement, mode, profile, lifetime, ReportRun);
    }

    public override void DidBecomeActive(UIScene scene) => _session?.Resume();

    public override void WillResignActive(UIScene scene) => _session?.Pause();

    public override void DidDisconnect(UIScene scene)
    {
        _session?.Pause();
        _session = null;
        _lifetime?.Cancel();
        _lifetime = null;
        _output = null;
        Window = null;
    }

    private async Task RunSessionAsync(MovementSession movement, string mode, string profile, CancellationTokenSource lifetime, Action<string> report)
    {
        var cancellation = lifetime.Token;
        try
        {
            await Task.Run(() => movement.RunAsync(mode, profile, cancellation), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            report("Conformance passed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine("Conformance cancelled.");
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            report($"Conformance failed: {failure.Message}");
        }
        finally
        {
            BeginInvokeOnMainThread(() =>
            {
                if (ReferenceEquals(_lifetime, lifetime))
                {
                    _lifetime = null;
                    _session = null;
                }

                lifetime.Dispose();
            });
        }
    }

    private void Report(string message, UITextView output, CancellationToken cancellation)
    {
        Console.WriteLine(message);
        BeginInvokeOnMainThread(() =>
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_output, output))
                Append(message);
        });
    }

    private void Append(string message)
    {
        if (_output is not { } output)
            return;

        string text = output.Text + "\n" + message;
        output.Text = text;
        output.ScrollRangeToVisible(new NSRange(text.Length, 0));
    }

    private static string ReadOption(string name, string defaultValue)
    {
        var arguments = AppDelegate.LaunchArguments;
        int index = Array.IndexOf(arguments, name);
        if (index < 0)
            return defaultValue;
        if (index + 1 == arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Expected a value after {name}.");
        return arguments[index + 1];
    }
}
