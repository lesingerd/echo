using System.Reflection;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// The few things the suites need from inside a live EchoTrayContext.
///
/// InternalsVisibleTo already exposes Echo's internal types, so nothing here needs reflection
/// for that reason. These particular members are <c>private</c>: they are the running state
/// machine, not an interface, and widening them purely for tests would leak test concerns into
/// the app. The reflection is instead confined to this one file, so a rename breaks in a single
/// obvious place rather than scattered through the suites.
/// </summary>
internal static class ContextProbe
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static EchoState State(EchoTrayContext context) => Read<EchoState>(context, "_state");

    public static string PendingText(EchoTrayContext context) => Read<string>(context, "_pendingText");

    public static ClipboardHistory History(EchoTrayContext context) => Read<ClipboardHistory>(context, "_history");

    public static ContextMenuStrip Menu(EchoTrayContext context) => Read<ContextMenuStrip>(context, "_menu");

    public static ToolStripMenuItem HistoryItem(EchoTrayContext context) => Read<ToolStripMenuItem>(context, "_historyItem");

    /// <summary>Exactly what a left click on the tray icon calls.</summary>
    public static void ArmForNextWindow(EchoTrayContext context) => Call(context, "ArmForNextWindow");

    public static void CancelPending(EchoTrayContext context, string message) => Call(context, "CancelPending", message);

    private static T Read<T>(EchoTrayContext context, string field) =>
        (T)(typeof(EchoTrayContext).GetField(field, Instance)
            ?? throw new MissingFieldException(nameof(EchoTrayContext), field))
            .GetValue(context)!;

    private static void Call(EchoTrayContext context, string method, params object[] args) =>
        (typeof(EchoTrayContext).GetMethod(method, Instance)
            ?? throw new MissingMethodException(nameof(EchoTrayContext), method))
            .Invoke(context, args);
}
