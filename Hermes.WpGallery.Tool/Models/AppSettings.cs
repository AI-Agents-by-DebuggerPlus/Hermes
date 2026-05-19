namespace Hermes.WpGallery.Tool.Models;

public class AppSettings
{
    // WordPress / REST
    public string WordPressUrl { get; set; } = string.Empty;
    public string SecretToken  { get; set; } = string.Empty;
    public string Channel      { get; set; } = "camera1";

    // Capture
    public int    IntervalSeconds  { get; set; } = 10;
    public int    JpegQuality      { get; set; } = 85;
    public int    MonitorIndex     { get; set; } = -1;  // -1 = all monitors
    public string ImageFormat      { get; set; } = "jpeg"; // jpeg | png | webp
    public bool   CaptureOnChange  { get; set; } = false;
    public int    ChangeThreshold  { get; set; } = 5;   // % pixels changed

    // Capture region (0 = full screen)
    public int RegionX      { get; set; } = 0;
    public int RegionY      { get; set; } = 0;
    public int RegionWidth  { get; set; } = 0;
    public int RegionHeight { get; set; } = 0;

    // Behaviour
    public bool StartWithWindows   { get; set; } = false;
    public bool MinimizeToTray     { get; set; } = false;
    public bool SendLogsToSite     { get; set; } = true;
    public bool AutoStartCapture   { get; set; } = false;
    public int  MaxRetries         { get; set; } = 3;
    public bool ShowPreview        { get; set; } = true;
}
