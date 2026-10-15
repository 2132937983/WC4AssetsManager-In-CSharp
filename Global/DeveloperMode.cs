namespace WC4MapEditor.Global;

public static class DeveloperMode
{
    private static bool _enabled = false;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
