using MelonLoader;

[assembly: MelonInfo(typeof(FontReplacerMod.Core), "FontReplacerMod", "1.0.0", "Tents89")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace FontReplacerMod;

public class Core : MelonMod
{
    private FontPatcher _patcher;

    public override void OnInitializeMelon()
    {
        _patcher = new FontPatcher(LoggerInstance);
        _patcher.Initialize();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"Scene loaded: {sceneName} (index {buildIndex})");
        _patcher.OnSceneLoaded();
    }
}
