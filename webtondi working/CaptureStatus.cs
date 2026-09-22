namespace WebToNdi
{
    /// <summary>Snapshot of what the capture is currently doing, for the management dashboard/API.</summary>
    internal sealed record CaptureStatus(
        string Url,
        int Width,
        int Height,
        int TargetFps,
        double MeasuredFps,
        string NdiName);
}
